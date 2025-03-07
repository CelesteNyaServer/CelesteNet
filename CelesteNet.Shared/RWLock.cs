using System;
using System.Threading;

namespace Celeste.Mod.CelesteNet
{
    public class RWLock : IDisposable {

        public readonly ReaderWriterLockSlim Inner = new(LockRecursionPolicy.SupportsRecursion);
        private readonly RLock _R;
        private readonly RULock _RU;
        private readonly WLock _W;
        private volatile bool _IsDisposed = false;

        public RWLock() {
            _R = new(Inner);
            _RU = new(Inner);
            _W = new(Inner);
        }

        public RLock R() => _R.Start();
        public RULock RU() => _RU.Start();
        public WLock W() => _W.Start();

        public void Dispose() {
            if (_IsDisposed)
                return;
            
            try {
                Inner.Dispose();
            } catch (Exception ex) {
                Logger.Log(LogLevel.WRN, "rwlock", $"释放RWLock时出错: {ex.Message}");
            } finally {
                _IsDisposed = true;
            }
        }

        public class RLock : IDisposable {
            public readonly ReaderWriterLockSlim Inner;
            private volatile bool _IsDisposed = false;

            public RLock(ReaderWriterLockSlim inner) {
                Inner = inner;
            }

            public RLock Start() {
                if (_IsDisposed || Inner.WaitingReadCount < 0) // 检查是否已释放
                    return this; // 如果已释放，直接返回，不尝试获取锁
                
                try {
                    if (!Inner.TryEnterReadLock(5000)) { // 5秒超时
                        Logger.Log(LogLevel.WRN, "rwlock", "获取读锁超时，可能存在死锁风险");
                        if (!Inner.TryEnterReadLock(0)) {
                            throw new TimeoutException("无法获取读锁，可能存在死锁");
                        }
                    }
                } catch (ObjectDisposedException) {
                    // 忽略已释放对象的异常
                    _IsDisposed = true;
                    Logger.Log(LogLevel.VVV, "rwlock", "尝试获取已释放的读锁");
                }
                return this;
            }

            public void Dispose() {
                if (_IsDisposed)
                    return;
                
                try {
                    if (Inner.IsReadLockHeld)
                        Inner.ExitReadLock();
                } catch (ObjectDisposedException) {
                    // 忽略已释放对象的异常
                } finally {
                    _IsDisposed = true;
                }
            }
        }

        public class RULock : IDisposable {
            public readonly ReaderWriterLockSlim Inner;
            private volatile bool _IsDisposed = false;

            public RULock(ReaderWriterLockSlim inner) {
                Inner = inner;
            }

            public RULock Start() {
                if (_IsDisposed || Inner.WaitingUpgradeCount < 0) // 检查是否已释放
                    return this; // 如果已释放，直接返回，不尝试获取锁
                
                try {
                    if (!Inner.TryEnterUpgradeableReadLock(5000)) { // 5秒超时
                        Logger.Log(LogLevel.WRN, "rwlock", "获取可升级读锁超时，可能存在死锁风险");
                        if (!Inner.TryEnterUpgradeableReadLock(0)) {
                            throw new TimeoutException("无法获取可升级读锁，可能存在死锁");
                        }
                    }
                } catch (ObjectDisposedException) {
                    // 忽略已释放对象的异常
                    _IsDisposed = true;
                    Logger.Log(LogLevel.VVV, "rwlock", "尝试获取已释放的可升级读锁");
                }
                return this;
            }

            public void Dispose() {
                if (_IsDisposed)
                    return;
                
                try {
                    if (Inner.IsUpgradeableReadLockHeld)
                        Inner.ExitUpgradeableReadLock();
                } catch (ObjectDisposedException) {
                    // 忽略已释放对象的异常
                } finally {
                    _IsDisposed = true;
                }
            }
        }

        public class WLock : IDisposable {
            public readonly ReaderWriterLockSlim Inner;
            private volatile bool _IsDisposed = false;

            public WLock(ReaderWriterLockSlim inner) {
                Inner = inner;
            }

            public WLock Start() {
                if (_IsDisposed || Inner.WaitingWriteCount < 0) // 检查是否已释放
                    return this; // 如果已释放，直接返回，不尝试获取锁
                
                try {
                    if (!Inner.TryEnterWriteLock(5000)) { // 5秒超时
                        Logger.Log(LogLevel.WRN, "rwlock", "获取写锁超时，可能存在死锁风险");
                        if (!Inner.TryEnterWriteLock(0)) {
                            throw new TimeoutException("无法获取写锁，可能存在死锁");
                        }
                    }
                } catch (ObjectDisposedException) {
                    // 忽略已释放对象的异常
                    _IsDisposed = true;
                    Logger.Log(LogLevel.VVV, "rwlock", "尝试获取已释放的写锁");
                }
                return this;
            }

            public void Dispose() {
                if (_IsDisposed)
                    return;
                
                try {
                    if (Inner.IsWriteLockHeld)
                        Inner.ExitWriteLock();
                } catch (ObjectDisposedException) {
                    // 忽略已释放对象的异常
                } finally {
                    _IsDisposed = true;
                }
            }
        }

    }
}
