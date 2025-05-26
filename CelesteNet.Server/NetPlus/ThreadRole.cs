using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server
{
    /*
    A thread role represents something a thread could be doing, e.g. waiting for
    TCP connections, polling sockets, flushing send queues, or just ideling. The
    thread pool contains a list of thread roles, and it will monitor their
    activity rates and heuristicaly distribute roles among all threads.
    -Popax21
    */
    public abstract class NetPlusThreadRole : IDisposable {

        public abstract class RoleWorker : IDisposable {
            private readonly ReaderWriterLockSlim ActivityLock;
            internal int ActiveZoneCounter = 0;
            private long LastActivityUpdate = long.MinValue;
            private float LastActivityRate = 0f;

            protected RoleWorker(NetPlusThreadRole role, NetPlusThread thread) {
                Role = role;
                Thread = thread;

                // 使用ReaderWriterLockSlim替代RWLock
                ActivityLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

                // 使用try-finally确保锁的释放
                try {
                    role.WorkerLock.Inner.EnterWriteLock();
                    role.Workers.Add(this);
                } finally {
                    if (role.WorkerLock.Inner.IsWriteLockHeld) {
                        role.WorkerLock.Inner.ExitWriteLock();
                    }
                }
            }

            public virtual void Dispose() {
                try {
                    Role.WorkerLock.Inner.EnterWriteLock();
                    Role.Workers.Remove(this);
                } finally {
                    if (Role.WorkerLock.Inner.IsWriteLockHeld) {
                        Role.WorkerLock.Inner.ExitWriteLock();
                    }
                    ActivityLock.Dispose();
                }
            }

            protected internal abstract void StartWorker(CancellationToken token);

            protected void EnterActiveZone() {
                try {
                    ActivityLock.EnterWriteLock();
                    Thread.Pool.IterateSteadyHeuristic(ref LastActivityRate, ref LastActivityUpdate, (ActiveZoneCounter++ > 0) ? 1f : 0f, true);
                } finally {
                    if (ActivityLock.IsWriteLockHeld) {
                        ActivityLock.ExitWriteLock();
                    }
                }
            }

            protected void ExitActiveZone() {
                try {
                    ActivityLock.EnterWriteLock();
                    if (ActiveZoneCounter <= 0)
                        throw new InvalidOperationException("Not in an active zone");
                    Thread.Pool.IterateSteadyHeuristic(ref LastActivityRate, ref LastActivityUpdate, (ActiveZoneCounter-- > 0) ? 1f : 0f, true);
                } finally {
                    if (ActivityLock.IsWriteLockHeld) {
                        ActivityLock.ExitWriteLock();
                    }
                }
            }

            public NetPlusThread Thread { get; }
            public NetPlusThreadRole Role { get; }

            public float ActivityRate {
                get {
                    try {
                        ActivityLock.EnterReadLock();
                        return Thread.Pool.IterateSteadyHeuristic(ref LastActivityRate, ref LastActivityUpdate, (ActiveZoneCounter > 0) ? 1f : 0f);
                    } finally {
                        if (ActivityLock.IsReadLockHeld) {
                            ActivityLock.ExitReadLock();
                        }
                    }
                }
            }
        }

        public NetPlusThreadPool Pool { get; }
        public float ActivityRate => EnumerateWorkers().Aggregate(0f, (a, w) => a + w.ActivityRate / Workers.Count);

        public abstract int MinThreads { get; }
        public abstract int MaxThreads { get; }

        private bool Disposed = false;
        private readonly RWLock WorkerLock = new();
        private readonly List<RoleWorker> Workers = new();

        protected NetPlusThreadRole(NetPlusThreadPool pool) {
            Pool = pool;
        }

        public virtual void Dispose() {
            if (Disposed)
                return;
            Disposed = true;

            using (WorkerLock.W()) {
                Workers.Clear();
                WorkerLock.Dispose();
            }
        }

        public virtual void InvokeSchedular() {}

        public IEnumerable<RoleWorker> EnumerateWorkers() {
            using (WorkerLock.R())
            foreach (RoleWorker worker in Workers)
                yield return worker;
        }

        public RoleWorker? FindWorker(Func<RoleWorker, bool> filter){
            foreach (RoleWorker worker in EnumerateWorkers()) {
                if (filter(worker))
                    return worker;
            }
            return null;
        }

        public abstract RoleWorker CreateWorker(NetPlusThread thread);

    }

    public sealed class IdleThreadRole : NetPlusThreadRole {

        private sealed class Worker : RoleWorker {
            public Worker(IdleThreadRole role, NetPlusThread thread) : base(role, thread) {}

            protected internal override void StartWorker(CancellationToken token) => token.WaitHandle.WaitOne();
        }

        public override int MinThreads => 0;
        public override int MaxThreads => int.MaxValue;

        public IdleThreadRole(NetPlusThreadPool pool) : base(pool) {}

        public override NetPlusThreadRole.RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);

    }
}