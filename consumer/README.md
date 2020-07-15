<!-- markdownlint-disable MD033 -->

# Consumer

The consumer service is responsible for fetching records from Event Hubs and committing them to Cosmos. This document will describe the custom code that was implemented to change the default behaviors.

## AllowBulkExecution

The Cosmos client has AllowBulkExecution set to TRUE. This setting changes the way the INSERT, UPDATE, and UPSERT commands work by making them queue the requests instead of processing them immediately. The records are queued per logical partition and dispatched as a batch once one of these conditions is true: (a) 100 records are queued for dispatch to the same logical partition, or (b) at least one record in the logical partition queue has been waiting for 1 second.

Given the common scenario of tens of thousands of logical partitions, it is unlikely to fill the batch with 100 messages, mostly it is going to be flushed with a small number. If the data rate is only a few thousand records per second, it is likely that most batches are only a single record. However, there is really no downside to using this mode for bulk-import as it only adds up to 1 second of latency for your run and it might provide some efficiency.

## Rate-Limiter

The goal of the rate-limiter is to allow you to configure a number of records to process per second from Event Hubs. Provided there are enough records and the consumer is given enough compute and memory to process at that speed or greater, you can be assured that exactly that number of records per second is processed (no more or less). This is desirable because if you know the exact rate of message processing and you know the RUs required to process each message, you can know exactly how many RUs Cosmos needs to be configured to handle. For example, if you have 4 consumers, each limited to 1000 records per sec, and each message takes 6 RU to commit, you would require Cosmos to be configured with at least 24,000 RUs.

:warning: You need a large number of logical partitions for Cosmos to distribute the data evenly. If you have an uneven distribution, you will need to overprovision RUs.

The rate-limiter works in this way:

* A time period is chosen as either 1 second (if the rate is >= 10) or 100 milliseconds (if it is < 10).

* A number of slots is allocated as the number of records per second you have chosen.

* Every time period "x" slots are released, where "x" is equal to either <sup>1</sup>/<sub>10</sub>th of the total slots (if the rate is >= 10) or the total number of slots (if it is < 10).

* Multiple partitions are constantly raising record received events, but within a partition these are synchronous (i.e. a partition cannot raise more than 1 event at a time). When an event is raised, either a free slot is available and the record is immediately processed, or that pipeline is blocked until a slot is made available.

## Checkpointing

Records are being delivered from Event Hub by partition in order for a Consumer Group. Once records are successfully processed we need to checkpoint the partition to tell it that those records should not be delivered again. To reiterate, this checkpointing process is specific to the Consumer Group and partition (i.e. other Consumer Groups can process at different rates, different partitions will be checkpointed at different points, etc.). When a record is delivered to the consumer, it is queued to the Cosmos bulk processor, but those records will be committed out of order (see the AllowBulkExecution section for how the records are flushed). Of course, this creates a problem because we desire to checkpoint the furthest point for which all records have been committed. The WorkList contains WorkItems and solves this challenge.

There is one WorkList per Event Hub partition assigned to this consumer.

The Add() function is called to add each WorkItem (record) that needs to be tracked. Every 5 seconds, the Checkpoint() function is called for each WorkList.

The Checkpoint() function finds the offset of the furthest consecutive completed record and checkpoints at that offset. For example, if the offset started at 0 and records with offset 0, 1, 2, 5, 6, 7 were completed, then the furthest consecutive offset is 2 and it would be checkpointed there.

| 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| :white_check_mark: | :white_check_mark: | :white_check_mark: | :x: | :x: | :white_check_mark: | :white_check_mark: | :white_check_mark: |
| | | :arrow_up_small: | | | | | |

Once 3 and 4 are completed, the checkpoint would be committed at offset 7.

A WorkItem is "completed" once it has confirmation that the record was successfully committed or failed to commit. A failure is a status code outside of the 200-range but not a 429 (TooManyRequests). A 429 will be retried as discussed below.

:warning: This behavior of checkpointing after success/failure is a common pattern, however, it is different than the standard Event Hub binding for Functions. The Function binding checkpoints whether or not the records are processed. For example, if the Function is canceled or crashes when executing, as long as the Function runtime is still running, the records will be checkpointed. This is done so that the Event Hub processing is never halted, it continues processing a stream of data, but it does mean that some records may never be processed. In other words, this approach will process all records at least once and maybe more. The Function binding may or may not process all records and it may process them more than once.

## Retry Logic

The Cosmos client is set to MaxRetryAttemptsOnRateLimitedRequests=0 meaning there are no automatic retries. However, the WorkItem Process() method implements retries when a 429 (TooManyRequests) is received or the Task is canceled. Retries wait for 2 seconds between attempts and they will continue forever. Retry forever ensures that eventually the record will be completed (success or failure) or the ALLOW_BACKLOG_OF (default: 320k) or ALLOW_BACKLOG_TO (default: 10 minutes) limits will be exceeded and the process will throw an exception.
