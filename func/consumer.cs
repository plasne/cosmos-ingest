using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DataModel;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace func
{
    public static class consumer
    {
        [FunctionName("consumer")]
        public static async Task Run(
            [EventHubTrigger("ingest", Connection = "EventHubConnectionAppSetting", ConsumerGroup = "$Default")]
            EventData[] events,
            [CosmosDB(databaseName: "db", collectionName: "container", ConnectionStringSetting = "CosmosDBConnection")]
            IAsyncCollector<MyDoc> output,
            ILogger log
        )
        {
            var exceptions = new List<Exception>();

            foreach (EventData eventData in events)
            {
                try
                {

                    // deserialize
                    var msg = JsonSerializer.Deserialize<MyDoc>(eventData.Body.Array);

                    // add to output
                    await output.AddAsync(msg);

                    // log
                    //log.LogInformation($"C# Event Hub trigger function processed a message: {msg.id} {msg.locId}");
                    await Task.Yield();

                }
                catch (Exception e)
                {
                    // We need to keep processing the rest of the batch - capture this exception and continue.
                    // Also, consider capturing details of the message that failed processing so it can be processed again later.
                    exceptions.Add(e);
                }
            }

            // Once processing of the batch is complete, if any messages in the batch failed processing throw an exception so that there is a record of the failure.

            if (exceptions.Count > 1)
                throw new AggregateException(exceptions);

            if (exceptions.Count == 1)
                throw exceptions.Single();
        }
    }
}
