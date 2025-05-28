using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();
    private static UnityMainThreadDispatcher _instance = null;

    public static UnityMainThreadDispatcher Instance()
    {
        if (_instance == null)
        {
            var go = new GameObject("UnityMainThreadDispatcher");
            _instance = go.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(go);
        }
        return _instance;
    }

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("UnityMainThreadDispatcher initialized");
        }
        else if (_instance != this)
        {
            Debug.Log("Duplicate UnityMainThreadDispatcher destroyed");
            Destroy(gameObject);
        }
    }

    // Separate method for race-specific initialization
    private void InitializeRaceSystems()
    {
        Debug.Log("Initializing race systems");
        // Any race-specific setup that should only happen in race scenes
    }

    void Update()
    {
        lock(_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }

    public void Enqueue(Action action)
    {
        lock(_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }

    public Task EnqueueAsync(Action action)
    {
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
}