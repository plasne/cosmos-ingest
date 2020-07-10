using System;
using dotenv.net;
using Azure.Storage.Blobs;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Azure.Cosmos;
using System.Threading;
using System.Collections.Generic;
using System.Linq;

namespace consumer
{

    class Program
    {

        static async Task Main(string[] args)
        {

            // variables
            DotEnv.Config(false);
            var eventHubConnString = System.Environment.GetEnvironmentVariable("EVENTHUB_CONNSTRING");
            var eventHubName = System.Environment.GetEnvironmentVariable("EVENTHUB_NAME");
            var eventHubConsumerGroup = System.Environment.GetEnvironmentVariable("EVENTHUB_CONSUMERGROUP");
            if (string.IsNullOrEmpty(eventHubConsumerGroup)) eventHubConsumerGroup = EventHubConsumerClient.DefaultConsumerGroupName;
            var eventHubRateLimitString = System.Environment.GetEnvironmentVariable("EVENTHUB_RATELIMIT");
            if (!int.TryParse(eventHubRateLimitString, out int eventHubRateLimit)) eventHubRateLimit = 0;
            var blobConnString = System.Environment.GetEnvironmentVariable("BLOB_CONNSTRING");
            var blobContainer = System.Environment.GetEnvironmentVariable("BLOB_CONTAINER");
            var cosmosConnString = System.Environment.GetEnvironmentVariable("COSMOS_CONNSTRING");
            var cosmosDatabase = System.Environment.GetEnvironmentVariable("COSMOS_DATABASE");
            var cosmosContainer = System.Environment.GetEnvironmentVariable("COSMOS_CONTAINER");

            // variable: determines whether data is written to cosmos or not (defaults to TRUE)
            var cosmosWrite = System.Environment.GetEnvironmentVariable("COSMOS_WRITE")?.ToLower();
            bool writeToCosmos = !(new string[] { "no", "false", "n", "0" }.Contains(cosmosWrite));

            // variable: allow up to "x" workitems to be buffered across all partitions
            var allowBacklogOfString = System.Environment.GetEnvironmentVariable("ALLOW_BACKLOG_OF");
            if (!int.TryParse(allowBacklogOfString, out int allowBacklogOf)) allowBacklogOf = 32 * 10000; // 10k per possible partition

            // variable: allow a workitem to persist up to 10 minutes before it is critical error (in seconds)
            var allowBacklogToString = System.Environment.GetEnvironmentVariable("ALLOW_BACKLOG_TO");
            if (!int.TryParse(allowBacklogToString, out int allowBacklogTo)) allowBacklogTo = 10 * 60;

            // create connections to blob and eventhub
            var storageClient = new BlobContainerClient(blobConnString, blobContainer);
            var eventHubProcessor = new EventProcessorClient(storageClient, eventHubConsumerGroup, eventHubConnString, eventHubName);
            var client = new CosmosClient(cosmosConnString, new CosmosClientOptions()
            {
                AllowBulkExecution = true,
                MaxRetryAttemptsOnRateLimitedRequests = 0
            });
            var container = client.GetContainer(cosmosDatabase, cosmosContainer);

            // create the rate limiter
            var limiter = new RateLimiter(eventHubRateLimit);

            // start work lists, one per potential partition
            var workLists = new WorkList<MyDoc>[32];

            // create non-threadsafe counters
            // NOTE: if this isn't perfect, but it won't really matter
            double totalRu = 0.0;
            int count = 0;

            // create thread-safe counters
            int successes = 0;
            int failures = 0;
            int retries = 0;

            // create a watch to record how long it is taking
            var watch = new Stopwatch();

            // handle process
            // NOTE: per https://docs.microsoft.com/en-us/azure/event-hubs/event-processor-balance-partition-load#thread-safety-and-processor-instances,
            //  this event is guaranteed to be called sequentially within a partition.
            eventHubProcessor.ProcessEventAsync += async (args) =>
            {

                // wait for a slot
                await limiter.WaitForSlot();

                // start the time if it isn't already running
                if (!watch.IsRunning) watch.Start();

                // create the work item
                var workitem = new WorkItem<MyDoc>(args);

                // add to the appropriate worklist
                var partitionId = int.Parse(args.Partition.PartitionId);
                var worklist = workLists[partitionId];
                if (worklist == null)
                {
                    worklist = new WorkList<MyDoc>(partitionId);
                    workLists[partitionId] = worklist;
                }
                await worklist.Add(workitem);

                // process (let it run out-of-band)
                var task = workitem.Process(
                    (writeToCosmos) ? container : null,
                    () =>
                    { // success
                        totalRu += workitem.RequestCharge;
                        count++;
                        Interlocked.Increment(ref successes);
                    },
                    exception =>
                    { // failure
                        Console.WriteLine(exception);
                        Interlocked.Increment(ref failures);
                    },
                    () =>
                    { // retry
                        Interlocked.Increment(ref retries);
                    }
                ).ConfigureAwait(false);

            };

            // handle errors
            eventHubProcessor.ProcessErrorAsync += (args) =>
            {
                Console.WriteLine($"partition '{args.PartitionId}': an unhandled exception was encountered. This was not expected to happen...");
                Console.WriteLine(args.Exception.Message);
                return Task.CompletedTask;
            };

            // start the timer for checkpointing and reporting progress
            Timer checkpointTimer = null;
            checkpointTimer = new Timer(async (state) =>
            {

                // checkpoint everything
                var tasks = new List<Task<CheckpointResult>>();
                foreach (var worklist in workLists)
                {
                    if (worklist != null) tasks.Add(worklist.Checkpoint());
                }
                var results = await Task.WhenAll(tasks);

                // report on the results of checkpointing
                int backlog = 0;
                foreach (var result in results)
                {
                    if (result != null)
                    {
                        if (result.Offset > -1)
                        {
                            Console.WriteLine($"partition {result.Partition} was checkpointed at offset {result.Offset}, leaving {result.Remaining} remaining workitems.");
                        }
                        else if (result.Oldest.Add(new TimeSpan(0, 0, allowBacklogTo)) < DateTime.Now)
                        {
                            throw new Exception($"partition {result.Partition} had a workitem older than ALLOW_BACKLOG_TO ({allowBacklogTo}).");
                        }
                        else
                        {
                            // partition was not checkpointed
                        }
                        backlog += result.Remaining;
                    }
                }
                if (backlog > allowBacklogOf) throw new Exception("the number of workitems in the backlog exceeded ALLOW_BACKLOG_OF ({allowBacklogOf}).");

                // report progress
                var avg = (count > 0) ? (int)Math.Ceiling(totalRu / count) : 0;
                Console.WriteLine($"{successes} successes, {failures} failures, {retries} retries after {watch.Elapsed.TotalSeconds} seconds, avg RU cost is {avg}, backlog is {backlog}.");

                // set timer to run again
                checkpointTimer.Change(5000, Timeout.Infinite);

            }, null, 5000, Timeout.Infinite);

            // start processing
            await eventHubProcessor.StartProcessingAsync();
            Console.WriteLine("started processing...");

            // wait for exit
            Process.GetCurrentProcess().WaitForExit();

        }

    }
}
