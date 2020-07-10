using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs.Processor;
using Microsoft.Azure.Cosmos;

namespace consumer
{

    public enum WorkItemStatus
    {
        Running,
        NeedsRetry,
        Successful,
        Failed
    }

    public class WorkItem<T> where T : CosmosItem
    {

        private ProcessEventArgs processEventArgs;

        public WorkItem(ProcessEventArgs processEventArgs)
        {
            this.processEventArgs = processEventArgs;
        }

        public DateTime Started { get; } = DateTime.Now;

        public long Offset
        {
            get => processEventArgs.Data.Offset;
        }

        public long SeqNum
        {
            get => processEventArgs.Data.SequenceNumber;
        }

        public WorkItemStatus Status { get; set; } = WorkItemStatus.Running;

        public bool IsComplete
        {
            get => Status == WorkItemStatus.Successful || Status == WorkItemStatus.Failed;
        }

        public double RequestCharge { get; set; }

        public async Task Process(Container container, Action onSuccess, Action<Exception> onFailure, Action onRetry)
        {

            // deserialize
            var msg = await JsonSerializer.DeserializeAsync<T>(processEventArgs.Data.BodyAsStream);

            // upsert into cosmos
            if (container != null)
            {

                // start a processing loop that will try over and over again until successful
                bool processing = true;
                while (processing)
                {

                    // upsert
                    var task = container.UpsertItemAsync(msg, new PartitionKey(msg.key));

                    // handle response
                    await task.ContinueWith(itemResponse =>
                    {

                        // success
                        if (itemResponse.IsCompletedSuccessfully)
                        {
                            this.Status = WorkItemStatus.Successful;
                            this.RequestCharge = itemResponse.Result.RequestCharge;
                            onSuccess();
                            processing = false;
                            return;
                        }

                        // canceled (which shouldn't ever happen in this sample)
                        if (itemResponse.IsCanceled)
                        {
                            this.Status = WorkItemStatus.NeedsRetry;
                            onRetry();
                        }

                        // failure
                        AggregateException innerExceptions = itemResponse.Exception.Flatten();
                        CosmosException cosmosException = innerExceptions.InnerExceptions.FirstOrDefault(innerEx => innerEx is CosmosException) as CosmosException;
                        if (cosmosException != null)
                        {

                            // cosmos expection
                            if (cosmosException.StatusCode == HttpStatusCode.TooManyRequests)
                            {
                                this.Status = WorkItemStatus.NeedsRetry;
                                onRetry();
                            }
                            else
                            {
                                this.Status = WorkItemStatus.Failed;
                                onFailure(cosmosException);
                                processing = false;
                                return;
                            }

                        }
                        else
                        {

                            // non-cosmos exception
                            this.Status = WorkItemStatus.Failed;
                            onFailure(innerExceptions.InnerExceptions.FirstOrDefault());
                            processing = false;
                            return;

                        }

                    });

                    // wait and try again (the loop will continue)
                    // NOTE: we could look at the response header of "x-ms-retry-after-ms", but it doesn't much
                    //   matter what it says, we can just try in a couple of seconds since this is a batch.
                    if (processing)
                    {
                        await Task.Delay(2000);
                    }

                }

            }
            else
            {
                this.Status = WorkItemStatus.Successful;
                onSuccess();
            }

        }

        public Task Checkpoint()
        {
            return processEventArgs.UpdateCheckpointAsync();
        }





    }

}