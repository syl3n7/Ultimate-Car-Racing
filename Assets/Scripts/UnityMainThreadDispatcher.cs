using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

/// <summary>
/// Provides a thread-safe way to execute code on Unity's main thread from background threads
/// </summary>
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();
    private static UnityMainThreadDispatcher _instance = null;
    private static readonly object _lockObject = new object();
    private static readonly SemaphoreSlim _queueSemaphore = new SemaphoreSlim(1, 1);
    private static bool _applicationIsQuitting = false;

    public static UnityMainThreadDispatcher Instance()
    {
        if (_applicationIsQuitting)
        {
            Debug.LogWarning("UnityMainThreadDispatcher: Instance requested while application is quitting. Returning null.");
            return null;
        }

        if (_instance == null)
        {
            lock (_lockObject)
            {
                if (_instance == null)
                {
                    var go = new GameObject("UnityMainThreadDispatcher");
                    _instance = go.AddComponent<UnityMainThreadDispatcher>();
                    DontDestroyOnLoad(go);
                    Debug.Log("UnityMainThreadDispatcher initialized");
                }
            }
        }
        return _instance;
    }

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Debug.Log("Duplicate UnityMainThreadDispatcher destroyed");
            Destroy(gameObject);
        }
    }

    private void OnApplicationQuit()
    {
        _applicationIsQuitting = true;
    }

    void Update()
    {
        // Process a maximum number of items per frame to prevent frame drops
        const int maxItemsPerFrame = 20;
        int itemsProcessed = 0;
        
        _queueSemaphore.Wait();
        try
        {
            while (_executionQueue.Count > 0 && itemsProcessed < maxItemsPerFrame)
            {
                var action = _executionQueue.Dequeue();
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception in dispatched task: {ex}");
                }
                itemsProcessed++;
            }
        }
        finally
        {
            _queueSemaphore.Release();
        }
    }

    /// <summary>
    /// Enqueues an action to be executed on the main Unity thread
    /// </summary>
    /// <param name="action">The action to execute</param>
    public void Enqueue(Action action)
    {
        if (action == null)
        {
            Debug.LogError("UnityMainThreadDispatcher: Cannot enqueue null action");
            return;
        }

        _queueSemaphore.Wait();
        try
        {
            _executionQueue.Enqueue(action);
        }
        finally
        {
            _queueSemaphore.Release();
        }
    }

    /// <summary>
    /// Enqueues an action to be executed on the main Unity thread and returns a Task that completes when the action is executed
    /// </summary>
    /// <param name="action">The action to execute</param>
    /// <returns>A Task that completes after the action is executed on the main thread</returns>
    public Task EnqueueAsync(Action action)
    {
        if (action == null)
        {
            Debug.LogError("UnityMainThreadDispatcher: Cannot enqueue null action");
            return Task.FromException(new ArgumentNullException(nameof(action)));
        }
        
        var tcs = new TaskCompletionSource<bool>();
        
        Enqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        
        return tcs.Task;
    }

    /// <summary>
    /// Executes a function on the main thread and returns the result
    /// </summary>
    /// <typeparam name="T">The return type of the function</typeparam>
    /// <param name="func">The function to execute</param>
    /// <returns>A Task that provides the function result when completed</returns>
    public Task<T> EnqueueFunc<T>(Func<T> func)
    {
        if (func == null)
        {
            Debug.LogError("UnityMainThreadDispatcher: Cannot enqueue null function");
            return Task.FromException<T>(new ArgumentNullException(nameof(func)));
        }
        
        var tcs = new TaskCompletionSource<T>();
        
        Enqueue(() =>
        {
            try
            {
                var result = func();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        
        return tcs.Task;
    }
}