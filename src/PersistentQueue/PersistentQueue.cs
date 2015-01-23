﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SQLite;
using System.IO;

namespace PersistentQueue
{
	public class Queue : IDisposable
	{
		#region Private Properties

		protected const string defaultQueueName = "persistentQueue";
        protected SQLite.SQLiteConnection store;
        protected bool disposed = false;

		#endregion

		#region Public Properties

        public string Name { get; protected set; }

		#endregion

		#region Static

		private static Dictionary<string, Queue> queues = new Dictionary<string, Queue>();

		public static Queue Default
		{
			get
			{
				return Create(defaultQueueName);
			}
		}

		public static Queue CreateNew()
		{
			return CreateNew(defaultQueueName);
		}

		public static Queue CreateNew(string name)
		{
			lock (queues)
			{
				if (queues.ContainsKey(name))
					throw new InvalidOperationException("there is already a queue with that name");

				var queue = new Queue(name, true);
				queues.Add(name, queue);

				return queue;
			}
		}

		public static Queue Create(string name)
		{
			lock (queues)
			{
				Queue queue;

				if (!queues.TryGetValue(name, out queue))
				{
					queue = new Queue(name);
					queues.Add(name, queue);
				}

				return queue;
			}
		}

		#endregion

        protected Queue(string name, bool reset = false)
		{
			if (reset && File.Exists(defaultQueueName))
				File.Delete(defaultQueueName);

			Initialize(name);
		}

		~Queue()
		{
			if (!disposed)
			{
				this.Dispose();
			}
		}

        protected void Initialize(string name)
		{
			Name = name;
			store = new SQLiteConnection(name);
			store.CreateTable<QueueItem>();
		}

		public void Enqueue(object obj)
		{
			lock (store)
			{
				store.Insert(obj.ToQueueItem());
			}
		}

		public QueueItem Dequeue(bool remove = true, int invisibleTimeout = 30000)
		{
			lock (store)
			{
				var item = GetNextItem();

				if (null != item)
				{
                    if (remove)
                    {
                        this.Delete(item);
                    }
                    else
                    {
                        this.Invalidate(item, invisibleTimeout);
                    }

					return item;
				}
				else
				{
					return null;
				}
			}
		}

        public virtual void Invalidate(QueueItem item, int invisibleTimeout = 30000)
        {
            item.InvisibleUntil = DateTime.Now.AddMilliseconds(invisibleTimeout);
            store.Update(item);
        }

		public virtual void Delete(QueueItem item)
		{
			store.Delete(item);
		}

		public object Peek()
		{
			lock (store)
			{
				var item = GetNextItem();
				
				return null == item ? null : item.ToObject();
			}
		}

		public T Peek<T>()
		{
			return (T)Peek();
		}

		public void Dispose()
		{
			if (!disposed)
				lock (queues)
				{
					disposed = true;

					queues.Remove(this.Name);
					store.Dispose();

					GC.SuppressFinalize(this);
				}
		}

        protected QueueItem GetNextItem()
		{
			return store.Table<QueueItem>()
					.Where(a => DateTime.Now > a.InvisibleUntil)
					.OrderBy(a => a.Id)
					.FirstOrDefault();
		}
	}

	public class QueueItem
	{
		[PrimaryKey]
		public long Id { get; protected set; }

		[Indexed]
		public DateTime InvisibleUntil { get; set; }

		public byte[] Message { get; set; }

		public QueueItem()
		{
			Id = DateTime.Now.Ticks;
		}

		public T CastTo<T>()
		{
			return (T)this.ToObject();
		}
	}
}
