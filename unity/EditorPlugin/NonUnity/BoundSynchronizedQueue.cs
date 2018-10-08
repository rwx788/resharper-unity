using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace JetBrains.Rider.Unity.Editor.NonUnity
{
  public class BoundSynchronizedQueue<T> where T : class
  {
    private readonly int myMaxSize;
    private readonly Queue<T> myQueue;
    private readonly object myLockObject = new object();
    public BoundSynchronizedQueue(int maxSize)
    {
      if (maxSize < 0)         
        throw new ArgumentOutOfRangeException(nameof (maxSize), "ArgumentOutOfRange_NeedNonNegNum");
      
      myMaxSize = maxSize;
      myQueue = new Queue<T>();
    }

    [CanBeNull]
    public T Dequeue()
    {
      lock (myLockObject)
      {
        return myQueue.Any() ? myQueue.Dequeue() : null;
      }
    }

    public void Enqueue([NotNull]T input)
    {
      lock (myLockObject)
      {
        myQueue.Enqueue(input);
        if (myQueue.Count >= myMaxSize)
          myQueue.Dequeue(); // limit max size  
      }
    }
  }
}