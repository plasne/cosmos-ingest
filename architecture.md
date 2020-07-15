<!-- markdownlint-disable MD033 -->

# Architecture

## Microservice vs Function

For the purposes of this section, I am using the word "microservice" to describe the approach of using the C# SDKs directly for each Azure service and hosting the solution in AKS. Of course, this is a type of microservice approach and there are many others, but I needed a way to differentiate this from the Function approach.

| Benefits of microservice   | Benefits of Functions |
| -------------------------- | --------------------- |
| Cheaper <sup>1</sup> | Easier to code <sup>2</sup> |
| Stream vs. Batch <sup>3</sup> | |
| Configurable throughput <sup>4</sup> | |
| Predictable RU consumption <sup>5</sup> | |
| More scalable <sup>6</sup> | |

<sup>1</sup> There is no cost for the AKS service, you only pay for the underlying compute, whereas there is a significant upcharge for App Service. In addition, you can determine the density of services on AKS, whereas App Services is always a single instance per node.

<sup>2</sup> Functions already come with a hosting environment, an opinionate way to handle logging and variables, advanced bindings, etc. You can write less code with less domain-specific knowledge in a Function.

<sup>3</sup> The microservice is doing the work using a stream methodology; records are constantly coming in and being dispatched at a predetermined rate. A Function reads a batch of messages, processes them completely, closes down, and then starts up again for the next batch. There is overhead in starting and stopping a process hundreds or thousands of times that doesn't exist in the stream approach.

<sup>4</sup> The microservice code is written to support rate-limiting - you configure it to process x messages per second. Provided the instances are fast enough to support that throughput, you will get exactly that number of messages processed per second. Depending on how you host the Function, you have some to no control over how fast data is processed. Using a dedicated App Service, you can determine the number of instances of a Function that could be running, you could determine the size of those instances, you could delay the completion of a Function. Those levers provide some control over throughput, but not as simple or granular as setting a rate.

<sup>5</sup> Given that the microservice approach commits exactly x messages per second, you can determine the RU consumption you want to pay for and then configure the microservice to deliver just under that cap. Rather with Functions, after you configure the number and types of instances, you need to benchmark to see how many RUs are consumed and provision that. This is more like the tail wagging the dog; again you have some control over throughput but it is not very exact.

<sup>6</sup> You can run far more replicas of a Pod in Kubernetes than you can run instances in App Service. 50k RU is a low enough bar that this does not come into play.

## Event Hub Guidance

* You should use a power-of-two for your number of partitions (1, 2, 4, 8, 16, 32).

* Within a Consumer Group, you can have multiple partitions per consumer, but a partition cannot be assigned to more than one consumer.

* You should consider the following advice (best-to-worst) when determining the right number of consumers in a Consumer Group:

    1. Use the same number of consumers as you have partitions.

    2. Have the same number of partitions per consumer. If you have 8 partitions, you could have 4 consumers for 2/2/2/2, but not 3 consumers for 3/3/2.

    3. Have the most even distribution possible. If you have 16 partitions with 3 consumers, you have 5/5/6 or a single consumer is responsible for 20% more. This is more even than 16 partitions with 5 consumers, where you have 3/3/3/3/4 or a single consumer is responsible for 33% more.

    4. Have more consumers rather than less (up to the number of partitions). At least if each consumer is responsible for less partitions, an imbalance is less impactful.

* Messages are at-least-once delivery which means that they could appear to the consumer more than once. The easiest way to handle that for this scenario was to use an UPSERT.

* Generally you should not have consumers ignoring message types. Rather split the messages at ingestion into different Event Hubs. Consumers that have to retrieve messages and dismiss them are consuming network bandwidth and processing time unnecessarily.

## Cosmos Guidance

* Step one in designing a NoSQL system is to figure out all exact queries you want to run. Build your data model based on those queries.

* Read up on building data models for Cassandra - it is much more rigid than Cosmos, but the same principals of design apply.

* Index only the properties you intend to filter on. The default policy indexes every property, this can signficantly increase the number of RUs required for each write operation.

* You should know exactly how much an INSERT and an UPDATE cost in RU for each document type. This will help you determine how many RUs you should provision. An UPSERT will have the same costs depending on what action is actually taken.

* Use a partition key with a high cardinality (a wide range of values). For 4.2m records, I wanted to see tens of thousands of possible values and maybe even hundreds of thousands. I chose 70,000 for these tests. Having a high cardinality ensures that the logical partitions are evenly distributed between physical partitions. RUs are even distributed between all physical partitions so you need your workload to read and write uniformly to get the best performance.

  * To get high cardinality with even distribution, you should consider adding a random component to the partition key. The customer had originally used a location id as the key, but they only had 700 locations. I added a randomly generated number from 001-100 to the end of location id. This upgrades the key space from 700 possible values to 70,000. With a large enough number of records, this will give you a very even distribution.

  * I like to have less than 100 records per logical partition. In this case, 4.2m records divided by 70k yields 60 records per partition, a reasonable number.

* Particularly with Functions, which are less predictable in how much work is running in any given second, you should expect some 429s (TooManyRequests) even if you are right-sized. It could even be several thousand per minute, but they shouldn't be sustained - you should get some spikes of 429 and then have periods with no 429s.

* If you are using the microservices approach with rate-limiting, you should be able to exactly calculate the RU requirement:

    ```bash
    (RU to insert a record) x (num of records per second) = (total RU requirement)
    ```

* Do not use CreateDatabaseIfNotExistsAsync or CreateContainerIfNotExistsAsync. Instead, assume the database and container exist and let an exception be thrown if they don't. If you want to catch the exception and handle creation at that point, it is fine. This will change 2 service calls into 0 service calls for the normal operation path.

* Pass in the partition key when calling INSERT, UPDATE, or UPSERT; this saves the SDK from having to lookup which column is used for the key.

    ```bash
    var task = container.UpsertItemAsync(msg, new PartitionKey(msg.key));
    ```

* :warning: There was some confusion with the customer around Cosmos stored procedures. A stored procedure can only process records within a single logical partition of a single container.

* My general advice with Cosmos is for your service to write data with as much parallelism as practical. In this particular case, I accomplish that by using the SDK which opens a bunch of ports for parallel writes and by using multiple replicas.

* There should be a reason to break data into separate containers (security boundary, different RU requirement, etc.), otherwise, put everything in the same.

* :warning: You can scale up Cosmos to process a batch and scale it back down afterwards, however, Cosmos only does partition splits, not merges on the physical partitions. Once you have a physical partition it can never have below 400 RUs (or 500 RUs if you have more than 4 containers in the database). All this is to say that if you scale up, there is a floor by which you cannot scale lower. A good rule of thumb is that you cannot scale down lower than 1/10th capacity (though the exact number will vary based on the number of physical partitions).

## Function Guidance

* Logging messages to App Insights can be time-consuming; every one of those messages has to be sent and received by a remote server. You should not log every message that comes through, maybe just log 1 or 2 messages for the batch.

* You should design Functions as simple as possible: input, process, output. You should scale them by running more of them in parallel instead of making one that has a more advanced input and output scheme (i.e. using a library for multi-threading or similar).

* Generally a Function should use bindings instead of an SDK. In this particular case, the Cosmos SDK proved much faster than the binding, so it was used, but try the binding first.
