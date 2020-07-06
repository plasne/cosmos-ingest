using System;
using System.Threading;
using System.Threading.Tasks;

namespace consumer
{

    public class RateLimiter
    {

        private int operationsPerSecond;
        private SemaphoreSlim semaphore;
        private Timer resetTimer;

        public RateLimiter(int operationsPerSecond)
        {
            this.operationsPerSecond = operationsPerSecond;
            this.semaphore = new SemaphoreSlim(operationsPerSecond, operationsPerSecond);
            int increment = (operationsPerSecond >= 10)
                ? 100
                : 1000;
            var numToRelease = (operationsPerSecond >= 10)
                ? (int)Math.Ceiling((double)operationsPerSecond / (double)10)
                : operationsPerSecond;
            this.resetTimer = new Timer(_ =>
            {
                int max = operationsPerSecond - this.semaphore.CurrentCount;
                if (max > 0)
                {
                    int count = Math.Min(max, numToRelease);
                    this.semaphore.Release(count);
                }
            }, null, increment, increment);
        }

        public Task WaitForSlot()
        {
            return this.semaphore.WaitAsync();
        }

    }

}