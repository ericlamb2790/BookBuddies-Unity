using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

namespace BookBuddies
{
    /// <summary>Lets code write "await request.SendWebRequest();".</summary>
    public static class AsyncOps
    {
        public static TaskAwaiter GetAwaiter(this AsyncOperation op)
        {
            var done = new TaskCompletionSource<bool>();
            if (op.isDone) done.SetResult(true);
            else op.completed += _ => done.TrySetResult(true);
            return ((Task)done.Task).GetAwaiter();
        }
    }
}
