<!-- markdownlint-disable MD033 -->

# Message Delivery

This document will describe some characteristics of message delivery as it relates to Event Hubs and Cosmos.

## At-Least-Once

Event Hubs is an at-least-once delivery system, meaning you should always see a record at least one time, but you might see it multiple times.

Since you may see a record more than once, you need to handle that case appropriately. In this case, UPSERTs should be used to commit records so that even if a commit happens more than once, the data is still accurate.

This applies only to Event Hubs, not to the processing of the messages by Cosmos. Let me explain why that is relevant...

* In the consumer microservice, checkpoints are only committed for records that have completed<sup>1</sup>. For instance, if there is an exception in your code, the next process to be assigned this partition will pick back up close to where it left off - maybe before the last commit, but not after it.

* In the consumer Function, batches are checkpointed whether or not the records are completed<sup>1</sup>. For instance, if there is an exception in your code, the records in the current batch will be checkpointed, but may have never been processed.

<sup>1</sup> Completed means the record is either successfully committed or it has thrown a non-transient error (i.e. it will never commit successfully). In either case, there is no reason to keep trying beyond this point, the result will be the same.

## Number of messages in -vs- number of messages out

The at-least-once section above explains a difference in the way checkpointing is done for the microservice and the Function. Therefore, if you ask the question, "if I put 10 messages into the Event Hub, will I always get 10 messages in Cosmos", the short answer is...

* consumer microservice: yes

* consumer Function: no

However, there are some cavaets and more information to consider...

### consumer microservice

* If a record receives a non-transient error when committing to Cosmos (for example, maybe it is missing the partition key or id), then the record will be skipped. In this case, there is no way the record could ever be committed successfully without modification.

* If a record being processed always results in an exception, the ingestion of that partition will be halted. For example, lets say a message cannot be deserialized and an attempt to do so crashes the process. It will continue to spin up and crash until Kubernetes does a CrashBackOff. You will need to fix this problem somehow manually or modify the code to detect this condition.

### consumer Function

* If a record receives a non-transient error when committing to Cosmos (for example, maybe it is missing the partition key or id), then the record will be skipped.

* If your code throws an exception but the Function runtime is still running, the inflight batch will be checkpointed regardless of whether some, none, or all of the records were processed. If there were problematic records, they have now been skipped.
