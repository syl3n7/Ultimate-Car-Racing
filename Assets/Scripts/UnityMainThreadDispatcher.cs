using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> executionQueue = new Queue<Action>();
    private static UnityMainThreadDispatcher instance = null;
    private static readonly object lockObject = new object();

    public static UnityMainThreadDispatcher Instance
    {
        get
        {
            if (instance == null)
            {
                // Check if an instance already exists in the scene
                instance = FindObjectOfType<UnityMainThreadDispatcher>();
                
                if (instance == null)
                {
                    // Create new GameObject if none exists
                    GameObject dispatcherObject = new GameObject("MainThreadDispatcher");
                    instance = dispatcherObject.AddComponent<UnityMainThreadDispatcher>();
                    DontDestroyOnLoad(dispatcherObject);
                }
            }
            return instance;
        }
    }

    /// <summary>
    /// Enqueues an action to be executed on the main game thread
    /// </summary>
    /// <param name="action">Action to be executed</param>
    public void Enqueue(Action action)
    {
        lock (lockObject)
        {
            executionQueue.Enqueue(action);
        }
    }

    /// <summary>
    /// Enqueues a coroutine to be executed on the main game thread
    /// </summary>
    /// <param name="action">Coroutine to be executed</param>
    public void EnqueueCoroutine(IEnumerator action)
    {
        lock (lockObject)
        {
            executionQueue.Enqueue(() =>
            {
                StartCoroutine(action);
            });
        }
    }

    private void Update()
    {
        lock (lockObject)
        {
            while (executionQueue.Count > 0)
            {
                Action action = executionQueue.Dequeue();
                try
                {
                    action?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error executing action on main thread: {e}");
                }
            }
        }
    }

    /// <summary>
    /// Helper method to check if we're on the main thread
    /// </summary>
    public static bool IsMainThread()
    {
        return System.Threading.Thread.CurrentThread.ManagedThreadId == 1;
    }

    private void OnDestroy()
    {
        lock (lockObject)
        {
            executionQueue.Clear();
        }
    }
}