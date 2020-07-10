using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataModel;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace func
{
    public static class bulk
    {
        [FunctionName("bulk")]
        public static async Task Run(
            [EventHubTrigger("ingest", Connection = "EventHubConnectionAppSetting", ConsumerGroup = "$Default")]
            EventData[] events,
            ILogger log,
            Microsoft.Azure.WebJobs.ExecutionContext context
        )
        {
            var exceptions = new List<Exception>();
            try
            {

                // add a configuration builder that works for local settings and env vars
                var config = new ConfigurationBuilder()
                    .SetBasePath(context.FunctionAppDirectory)
                    .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build();

                // create connections to blob and eventhub
                var cosmosConnstring = config["CosmosDBConnection"];
                var client = new CosmosClient(cosmosConnstring, new CosmosClientOptions()
                {
                    AllowBulkExecution = true,
                    MaxRetryAttemptsOnRateLimitedRequests = 999,
                    MaxRetryWaitTimeOnRateLimitedRequests = new TimeSpan(0, 15, 0)
                });
                var container = client.GetContainer("db", "container");

                // queue all messages to the bulk cosmos agent
                var tasks = new List<Task<ItemResponse<MyDoc>>>();
                foreach (EventData eventData in events)
                {
                    try
                    {

                        // deserialize
                        var msg = JsonSerializer.Deserialize<MyDoc>(eventData.Body.Array);

                        // add to output
                        var task = container.UpsertItemAsync(msg, new PartitionKey(msg.key));
                        tasks.Add(task);

                        // yield
                        await Task.Yield();

                    }
                    catch (Exception e)
                    {
                        exceptions.Add(e);
                    }
                }

                // wait on all commits, but yield every 1 second
                // NOTE: all samples include Yield(), though I cannot find any docs to say this is required
                int attempts = 0;
                log.LogInformation($"awaiting {tasks.Count} items to be committed to Cosmos...");
                while (true)
                {
                    Task work = Task.WhenAll(tasks);
                    Task timeout = Task.Delay(1000);
                    Task completed = await Task.WhenAny(work, timeout);
                    attempts++;
                    await Task.Yield();
                    if (completed == work) break;
                }

                // raise any exceptions found in the write tasks
                int canceled = 0;
                int failures = 0;
                foreach (var task in tasks)
                {
                    if (task.IsCanceled)
                    {
                        if (canceled < 1) exceptions.Add(new Exception("at least one Cosmos write was canceled, therefore the commit status is unknown."));
                        canceled++;
                    }
                    else if (task.IsFaulted)
                    {
                        failures++;
                        AggregateException innerExceptions = task.Exception.Flatten();
                        CosmosException cosmosException = innerExceptions.InnerExceptions.FirstOrDefault(innerEx => innerEx is CosmosException) as CosmosException;
                        if (cosmosException != null)
                        {
                            exceptions.Add(cosmosException);
                        }
                        else
                        {
                            exceptions.Add(innerExceptions.InnerExceptions.FirstOrDefault());
                        }
                    }
                }
                log.LogInformation($"{tasks.Count - canceled - failures} successes, {failures} failures, {canceled} cancellations after {attempts} attempts (one attempts per second).");

            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }

            // once processing of the batch is complete, if any messages in the batch failed processing throw an exception so that there is a record of the failure.
            if (exceptions.Count > 1) throw new AggregateException(exceptions);
            if (exceptions.Count == 1) throw exceptions.Single();

        }
    }
}
