using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using dotenv.net;

namespace producer
{
    class Program
    {

        static async Task Main(string[] args)
        {

            // get variables
            DotEnv.Config(false);
            var connstring = System.Environment.GetEnvironmentVariable("EVENTHUB_CONNSTRING");
            var name = System.Environment.GetEnvironmentVariable("EVENTHUB_NAME");
            var batchSizeString = System.Environment.GetEnvironmentVariable("EVENTHUB_BATCHSIZE");
            if (!int.TryParse(batchSizeString, out int batchSize)) batchSize = 100;
            var countString = System.Environment.GetEnvironmentVariable("EVENTHUB_COUNT");
            if (!int.TryParse(countString, out int count)) count = 100;

            // create the stopwatch and counters
            int success = 0;
            int failure = 0;
            var watch = new Stopwatch();

            // start the timer for reporting progress
            Timer timer = null;
            timer = new Timer((state) =>
            {

                // report progress
                Console.WriteLine($"{success} successes, {failure} failures, after {watch.Elapsed.TotalSeconds} seconds...");

                // set timer to run again
                timer.Change(5000, Timeout.Infinite);

            }, null, 5000, Timeout.Infinite);

            // create producer client
            await using (var producerClient = new EventHubProducerClient(connstring, name))
            {
                watch.Start();

                // process count
                for (int j = 0; j < count; j++)
                {

                    // add a batch
                    using (EventDataBatch eventBatch = await producerClient.CreateBatchAsync())
                    {
                        for (int i = 0; i < batchSize; i++)
                        {
                            var msg = MyDoc.Generate();
                            var bytes = JsonSerializer.SerializeToUtf8Bytes(msg);
                            if (eventBatch.TryAdd(new EventData(bytes)))
                            {
                                Interlocked.Increment(ref success);
                            }
                            else
                            {
                                Console.WriteLine($"failed {i} to add {msg.id} of {msg.locId}...");
                                Interlocked.Increment(ref failure);
                            }
                        }
                        await producerClient.SendAsync(eventBatch);
                    }

                }

            }

            // wait forever
            Console.WriteLine($"{success} successes, {failure} failures, after {watch.Elapsed.TotalSeconds} seconds, done.");
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            Process.GetCurrentProcess().WaitForExit();

        }

    }
}
