# Azure Function

This project is an attempt to use an Azure Function to get a consumer with similar performance to using the SDKs in a microservice.

There are 2 Functions in this project so that I could test performance both ways:

* consumer: This uses the Cosmos IAsyncCollector output binding.

* bulk: This uses the Cosmos 3.x+ SDK AllowBulkExecution flag.

## IAsyncCollector

Cancellations

##

S1
8 instances
50k RU
15k prefetch
5k batch

## Unpredictable Batch Size

Even though the Event Hub partitions were full of the 4.2m messages (16 partitions with ~262k messages each) before the Function consumer was started, any given execution of the Function might consider a full batch 5k or as small as 2 messages. See these screenshots of 2 executions at roughly the same time.

![empty](./empty.png)

![full](./full.png)

## Execution Speed

I looked at a lot of Function executions. If I divide the number of records processed by the execution time, I found the best performing execution processed about 100/sec and more commonly ~50/sec. Moreover, this is only consuming about 6.2k RU per physical partition or ~31k RU total. Therefore, I expect this execution to take about 2.5 hours.
