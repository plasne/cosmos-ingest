# Performance Tests

Here is a breakdown of the test runs...

| line # | scenario | execution time (sec) | record count | records/sec | notes |
| -----: | -------- | -------------------: | -----------: | ----------: | ----- |
| 1 | SDK/AKS - 4x d2v2, 1000/sec, 50k RU | 531 | 4,200,000 | 7,910 | |
| 2 | SDK/AKS - 4x d2v2, 3000/sec, 150k RU | 180 | 4,200,000 | 23,333 | only test of 150k RUs |
| 3 | func - 8x s1, 5k/15k batch, 50k RU, IAsyncCollector, per record logging | 5,520 | 1,707,953 | 309 | stopped early |
| 4 | func - 16x p1v2, 5k/15k batch, 50k RU, IAsyncCollector, per record logging | 6,360 | 4,122,842 | 648 | |
| 5 | func - 16x p1v2, 30k/90k batch, 50k RU, IAsyncCollector | 2,760 | 4,200,000 | 1522 | |
| 6 | func - 8x p1v2, 5k/15k batch, 50k RU, IAsyncCollector | 3,900 | 2,993,034 | 767 | stopped early |
| 7 | func - 8x p1v2, 30k/90k batch, 50k RU, IAsyncCollector | 5,100 | 4,200,000 | 824 | |
| 8 | func - 16x p1v2, 30k/90k batch, 50k RU, IAsyncCollector | 12,060 | 4,200,000 | 348 | |
| 9 | func - 16x p1v2, 30k/90k batch, 50k RU, IAsyncCollector | 2,220 | 3,326,143 | 1,498 | stopped early |
| 10 | func - 8x s1, 30k/90k batch, 50k RU, IAsyncCollector | 7,800 | 4,200,012 | 538 | too many records |
| 11 | func - 16x p1v2, 30k/90k batch, 50k RU, IAsyncCollector | 3,120 | 4,200,000 | 1,346 | |
| 12 | func - 16x p1v2, 30k/90k batch, 50k RU, IAsyncCollector | 2,160 | 4,200,000 | 1,944 | |
| 13 | func - 16x p1v2, 30k/90k batch, 50k RU, IAsyncCollector | 7,920 | 4,200,000 | 530 | |
| 14 | func - 16x p1v2, 30k/90k batch, 50k RU, IAsyncCollector | 2,580 | 4,200,000 | 1,628 | |
| 15 | func - 16x p1v2, 30k/90k batch, 50k RU, AllowBulkExecution | 540 | 4,185,174 | 7,750 | |
| 16 | func - 16x p1v2, 30k/90k batch, 50k RU, AllowBulkExecution | 540 | 4,200,000 | 7,778 | |
| 17 | func - 8x s1, 30k/90k batch, 50k RU, AllowBulkExecution | 780 | 4,181,400 | 5,361 | |
| 18 | func - 8x s1, 30k/90k batch, 50k RU, AllowBulkExecution | 840 | 4,160,285 | 4,953 | |
| 19 | func - 8x s1, 30k/90k batch, 50k RU, AllowBulkExecution, yields | 1,620 | 4,200,000 | 2,593 | |
| 20 | func - 8x s1, 30k/90k batch, 50k RU, AllowBulkExecution, yields | 1,320 | 4,064,019 | 3,079 | |
| 21 | func - 16x p1v2, 30k/90k batch, 50k RU, AllowBulkExecution, yields | 540 | 4,200,000 | 7,778 | |
| 22 | func - 4x p1v2, 30k/90k batch, 50k RU, AllowBulkExecution, yields | 540 | 4,199,900 | 7,778 | |
| 23 | func - 2x p1v2, 30k/90k batch, 50k RU, AllowBulkExecution, yields | 1,260 | 2,837,147 | 2,252 | barely progressing, too many errors (408, 503, etc.) |
| 24 | func - 4x p1v2, 30k/90k batch, 50k RU, AllowBulkExecution, yields | 540 | 4,200,000 | 7,778 | |

The tests were all conducted with the exact same messages in the exact same partitions. I simply deleted the checkpoint files before each run so that it would start over.

The Azure Functions were hosted in an App Service Plan manually scaled to the number of instances shown.

Here are some observations...

* Comparing line 1 to 15, 16, 21, 22, and 24 shows that we can achieve similar performance using Azure Functions to what was achieved with SDK/AKS (at least at 50k RU).

* The Event Hub Connector for Functions is designed to checkpoint whether or not all records in a batch were successfully processed. Here is the article describing why: [https://docs.microsoft.com/en-us/azure/azure-functions/functions-reliable-event-processing](https://docs.microsoft.com/en-us/azure/azure-functions/functions-reliable-event-processing). I do agree with the logic here - if messages definitely require delivery, then a queue is a better mechanism than a stream.

    :warning: This means you are not guaranteed to get the full 4.2m records committed. Most runs on the Premium SKU did record the full 4.2m, most runs on the Standard SKU did not.

* Line 23 shows what happens when you are underpowered. It got about 1/2 way through the batch but was throwing so many errors and getting so many failed executions, that I stopped it.

* Line 10 is the most concerning error. There are only 4.2m records, each with a unique id, but somehow Cosmos is showing 12 extra records. The only way I can think that this could happen is if the same unique id was stored in multiple partitions. The index should have caught that, but didn't. Regardless, this is definately a bug.

* For 50k RU, 4 p1v2 instances are just as good as 16 p1v2 instances as evidenced by lines 15, 16, and 21 vs. 22 and 24.

## Cost

Here are some sample prices for Linux in East US as of the writing of this article:

| qty. | VM type | cores | memory | cost |
| :--: | ------- | ----: | -----: | ---: |
| 4 | D2v2 | 8 | 28 GB | $339.02 |
| 4 | P1v2 | 4 | 14 GB | $294.92 |
| 6 | S1 | 6 | 10 GB | $416.10 |
| 8 | S1 | 8 | 14 GB | $554.80 |
| 16 | P1v2 | 16 | 56 GB | $1179.68 |

As we can see, it is also price comparible to consider 4 p1v2 instances and 4 d2v2 instances.
