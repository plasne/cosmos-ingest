using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace consumer
{

    public class CheckpointResult
    {

        public int Partition;

        public long Offset;

        public DateTime Oldest;

        public int Remaining;

    }

    public class WorkList<T> where T : CosmosItem
    {

        public List<WorkItem<T>> list = new List<WorkItem<T>>();

        private SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        public WorkList(int partition)
        {
            this.Partition = partition;
        }

        public int Partition { get; }

        public async Task Add(WorkItem<T> item)
        {
            // this adds the work item safely (checkpoint isn't running)
            await semaphore.WaitAsync();
            try
            {
                list.Add(item);
            }
            finally
            {
                semaphore.Release();
            }
        }

        private int FurthestComplete()
        {
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (!item.IsComplete) return i - 1;
            }
            return list.Count - 1;
        }

        public async Task<CheckpointResult> Checkpoint()
        {
            await semaphore.WaitAsync();
            try
            {

                // shortcut if there is nothing to shortcut
                if (list.Count < 1) return null;

                // sort
                list.Sort((a, b) =>
                {
                    return a.Offset.CompareTo(b.Offset);
                });

                // advance as much as we can
                int index = FurthestComplete();
                if (index < 0)
                {
                    return new CheckpointResult()
                    {
                        Partition = this.Partition,
                        Offset = -1,
                        Oldest = list[0].Started,
                        Remaining = list.Count
                    };
                }

                // checkpoint
                var cursor = list[index];
                await cursor.Checkpoint();

                // remove everything up to the furthest complete
                list.RemoveRange(0, index + 1);

                // return the result of checkpointing
                return new CheckpointResult()
                {
                    Partition = this.Partition,
                    Offset = cursor.Offset,
                    Oldest = (list.Count > 0) ? list[0].Started : DateTime.Now,
                    Remaining = list.Count
                };

            }
            finally
            {
                semaphore.Release();
            }
        }

    }

}