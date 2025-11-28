// Helpers/CompatibilityHelper.cs
namespace Nine.Design.Login.Helpers
{
    public static class CompatibilityHelper
    {
        /// <summary>
        /// 跨框架异步执行方法（适配 .NET 4.5 Task 用法）
        /// </summary>
        public static Task ExecuteAsync(Action action)
        {
#if NET45
            // .NET 4.5 用 Task.Factory.StartNew
            return Task.Factory.StartNew(action);
#else
            // .NET 4.6+ 用 Task.Run（更简洁）
            return Task.Run(action);
#endif
        }

        /// <summary>
        /// 跨框架异步执行带返回值的方法
        /// </summary>
        public static Task<T> ExecuteAsync<T>(Func<T> func)
        {
#if NET45
            return Task.Factory.StartNew(func);
#else
            return Task.Run(func);
#endif
        }


        public static class TaskHelper
        {
            public static Task GetCompletedTask()
            {
#if NET45 || NET46
                return Task.FromResult(0);
#else
        return Task.CompletedTask;
#endif
            }
        }
    }
}