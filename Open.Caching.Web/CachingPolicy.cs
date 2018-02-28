﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web;
using System.Web.Caching;

namespace Open.Caching.Web
{
	public struct CachingPolicy
	{
		public readonly ExpirationMode Mode;
		public readonly TimeSpan ExpiresAfter;
		public readonly CacheItemPriority Priority;
		public readonly TimeSpan ExpiresSliding;
		public DateTime ExpiresAbsolute => Mode == ExpirationMode.Absolute ? DateTime.Now.Add(ExpiresAfter) : Cache.NoAbsoluteExpiration;

		private CachingPolicy(ExpirationMode mode, TimeSpan expiresAfter, CacheItemPriority priority)
		{
			Mode = mode;
			ExpiresAfter = expiresAfter;
			Priority = priority;

			ExpiresSliding = mode == ExpirationMode.Sliding ? expiresAfter : Cache.NoSlidingExpiration;
		}

		public override bool Equals(object obj)
			=> obj is CachingPolicy policy
				&& Mode == policy.Mode
				&& ExpiresAfter.Equals(policy.ExpiresAfter)
				&& Priority.Equals(policy.Priority);

		public override int GetHashCode()
		{
			var hashCode = -486563138;
			hashCode = hashCode * -1521134295 + base.GetHashCode();
			hashCode = hashCode * -1521134295 + Mode.GetHashCode();
			hashCode = hashCode * -1521134295 + EqualityComparer<TimeSpan>.Default.GetHashCode(ExpiresAfter);
			hashCode = hashCode * -1521134295 + Priority.GetHashCode();
			return hashCode;
		}

		public static bool operator ==(CachingPolicy left, CachingPolicy right) => left.Equals(right);
		public static bool operator !=(CachingPolicy left, CachingPolicy right) => !left.Equals(right);


		public static CachingPolicy Slide(TimeSpan after, CacheItemPriority priority = CacheItemPriority.Default)
			=> new CachingPolicy(ExpirationMode.Sliding, after, priority);

		public static CachingPolicy Expire(TimeSpan after, CacheItemPriority priority = CacheItemPriority.Default)
			=> new CachingPolicy(ExpirationMode.Absolute, after, priority);

		public static CachingPolicy Slide(uint seconds, CacheItemPriority priority = CacheItemPriority.Default)
			=> Slide(TimeSpan.FromSeconds(seconds), priority);

		public static CachingPolicy Expire(uint seconds, CacheItemPriority priority = CacheItemPriority.Default)
			=> Expire(TimeSpan.FromSeconds(seconds), priority);

		public void Insert(string key, object value, CacheDependency dependencies = null, CacheItemRemovedCallback callback = null)
			=> HttpRuntime.Cache?.Insert(key, value, dependencies, ExpiresAbsolute, ExpiresSliding, Priority, callback);
		public void Insert(string key, object value, CacheItemRemovedCallback callback)
			=> Insert(key, value, null, callback);

		public object Add(string key, object value, CacheDependency dependencies = null, CacheItemRemovedCallback callback = null)
			=> HttpRuntime.Cache?.Add(key, value, dependencies, ExpiresAbsolute, ExpiresSliding, Priority, callback);
		public object Add(string key, object value, CacheItemRemovedCallback callback)
			=> Add(key, value, null, callback);

		static Task<T> ReturnAsTask<T>(object value)
		{
			if (value == null) return null;
			if (value is Task<T> task) return task;
			if (value is T v) return Task.FromResult(v);
			Debug.Fail("Actual type does not match expected for cache entry.");
			return null;
		}

		public Task<T> GetOrAddAsync<T>(string key, Func<T> factory, bool runSynchronously = false)
		{
			// Check if task/value exists first.
			var current = ReturnAsTask<T>(HttpRuntime.Cache?[key]);
			if (current != null) return current;

			// Setup the task.
			var task = new Task<T>(factory);

			// Attempt to add the task.  If current is not null, then return it (not ours).
			current = ReturnAsTask<T>(Add(key, task));

			if (current != null) return current;
			/* If current is null here, then either:
				1) Ours was used. https://msdn.microsoft.com/en-us/library/system.web.caching.cache.add(v=vs.110).aspx
				2) There is no HttpRuntime.Cache available.
				3) There is a value/type collision that will trigger Debug.Fail but not be fatal otherwise. */

			// We own the task.  Go ahead and run it.
			if (runSynchronously) task.RunSynchronously();
			else task.Start();

			return task;
		}

		public T GetOrAdd<T>(string key, Func<T> factory)
			=> GetOrAddAsync(key, factory, true).Result;
	}
}