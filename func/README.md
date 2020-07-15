# Azure Function

This project is an attempt to use an Azure Function to get a consumer with similar performance to using the SDKs in a microservice.

There are 2 Functions in this project so that I could test performance both ways:

* consumer: This uses the Cosmos IAsyncCollector output binding.

* bulk: This uses the Cosmos 3.x+ SDK AllowBulkExecution flag.

:information_source: You should use the bulk implementation, it is MUCH faster and does not have an issue with records delivered more than once.

## Unpredictable Batch Size

Even though the Event Hub partitions were full of the 4.2m messages (16 partitions with ~262k messages each) before the Function consumer was started, any given execution of the Function might consider a full batch 5k or as small as 2 messages. See these screenshots of 2 executions at roughly the same time.

![empty](./empty.png)

![full](./full.png)

## Execution Speed

I looked at a lot of Function executions. If I divide the number of records processed by the execution time, I found the best performing execution processed about 100/sec and more commonly ~50/sec. Moreover, this is only consuming about 6.2k RU per physical partition or ~31k RU total. Therefore, I expect this execution to take about 2.5 hours.

## Cancellations

Through testing, I determined that about 4% of Azure Function executions on an App Service Standard SKU were canceled by the Function runtime. No reason code is available, so I cannot say for sure what the issue is, but I assume it is a particular threshold that is being tripped (too much CPU utilization, too much memory utilization, too many faults of some kind, etc.).

The Premium SKU is not immune to these cancellations, but I only saw 1 cancellation out of dozens of runs, no where near 4%.

## At-Least-Once

Event Hubs delivers messages at-least-once, which means that you may see a record more than once. This could cause errors with the consumer implementation because it attempts to INSERT a record rather than UPSERT.
