using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HCore
{
    public static class EventBus
    {
        private class TempListEntry
        {
            public bool InUse;
            public List<IEventHandler> List;
        }

        private static readonly Dictionary<Type, List<IEventHandler>> _handlers = new();
        private static readonly Dictionary<Type, ISingleEventHandler> _singleHandlers = new();

        private static readonly List<TempListEntry> _tmpLists = new();


        public static void Invoke<THandler>(Action<THandler> callSelector) where THandler : IBaseEventHandler
        {
            if (typeof(ISingleEventHandler).IsAssignableFrom(typeof(THandler)))
            {
                if (_singleHandlers.TryGetValue(typeof(THandler), out ISingleEventHandler globalHandler))
                    callSelector((THandler)globalHandler);
                else
                    Debug.LogWarning($"{typeof(ISingleEventHandler)} of type {typeof(THandler)} is not register");
                return;
            }

            if (_handlers.TryGetValue(typeof(THandler), out List<IEventHandler> handlers))
            {
                var tmpList = GetTempHandlerList();
                try
                {
                    tmpList.AddRange(handlers);

                    foreach (var handler in tmpList)
                    {
                        if (handler != null)
                        {
                            callSelector((THandler)handler);
                        }
                    }
                }
                finally
                {
                    ReleaseTempHandlerList(tmpList);
                }
            }
        }

        public static TResult InvokeWithResult<THandler, TResult>(Func<THandler, TResult> callSelector) where THandler : IBaseEventHandler
            => InvokeWithResult<THandler, TResult>(callSelector, true);
        public static TResult InvokeWithResult<THandler, TResult>(Func<THandler, TResult> callSelector, TResult fallbackResult) where THandler : IBaseEventHandler
            => InvokeWithResult<THandler, TResult>(callSelector, false, fallbackResult);
        private static TResult InvokeWithResult<THandler, TResult>(Func<THandler, TResult> callSelector, bool logMissing, TResult fallbackResult = default) where THandler : IBaseEventHandler
        {
            TResult result = fallbackResult;

            if (typeof(ISingleEventHandler).IsAssignableFrom(typeof(THandler)))
            {
                if (_singleHandlers.TryGetValue(typeof(THandler), out ISingleEventHandler globalHandler))
                    result = callSelector((THandler)globalHandler);
                else if (logMissing)
                    Debug.LogWarning($"{typeof(ISingleEventHandler)} of type {typeof(THandler)} is not register");
                return result;
            }

            if (_handlers.TryGetValue(typeof(THandler), out List<IEventHandler> handlers))
            {
                var tmpList = GetTempHandlerList();
                try
                {
                    tmpList.AddRange(handlers);

                    foreach (var handler in tmpList)
                    {
                        if (handler != null)
                        {
                            result = callSelector((THandler)handler);
                        }
                    }
                }
                finally
                {
                    ReleaseTempHandlerList(tmpList);
                }
            }

            return result;
        }


        public static bool RegisterHandler<THandler>(THandler handler) where THandler : class, IEventHandler
        {
            if (!_handlers.TryGetValue(typeof(THandler), out List<IEventHandler> handlers))
            {
                handlers = new List<IEventHandler>();
                _handlers[typeof(THandler)] = handlers;
            }

            if (!handlers.Contains(handler))
            {
                handlers.Add(handler);
                return true;
            }

            return false;
        }
        public static bool UnregisterHandler<THandler>(THandler handler) where THandler : class, IEventHandler
        {
            if (_handlers.TryGetValue(typeof(THandler), out List<IEventHandler> handlers))
                return handlers.Remove(handler);

            return false;
        }


        public static bool RegisterSingleHandler<THandler>(THandler handler) where THandler : class, ISingleEventHandler
        {
            ISingleEventHandler prevHandler;
            if (_singleHandlers.TryGetValue(typeof(THandler), out prevHandler))
            {
                if (prevHandler != null)
                {
#if DEBUG
                    var prevHandlerUO = prevHandler as UnityEngine.Object;
                    var nextHandlerUO = handler as UnityEngine.Object;
                    var prevHandlerName = prevHandlerUO ? $"name: {prevHandlerUO.name}, type: {prevHandler.GetType().Name}" : $"type: {prevHandler.GetType().Name}, hashcode: {prevHandler.GetHashCode()}";
                    var nextHandlerName = nextHandlerUO ? $"name: {nextHandlerUO.name}, type: {handler.GetType().Name}" : $"type: {handler.GetType().Name}, hashcode: {handler.GetHashCode()}";

                    Debug.Assert(false, $"Bad code logic - trying to add more than one global handler for {typeof(THandler).Name}. Current handler: {prevHandlerName}, new handler: {handler}");
#endif
                    return false;
                }
            }

            _singleHandlers[typeof(THandler)] = handler;
            return true;
        }
        public static bool UnregisterSingleHandler<THandler>(THandler handler) where THandler : class, ISingleEventHandler
        {
            ISingleEventHandler prevHandler;
            if (_singleHandlers.TryGetValue(typeof(THandler), out prevHandler) && prevHandler == handler)
            {
                _singleHandlers.Remove(typeof(THandler));
                return true;
            }

            return false;
        }


        private static List<IEventHandler> GetTempHandlerList()
        {
            List<IEventHandler> list = null;

            foreach (var entry in _tmpLists)
            {
                if (!entry.InUse)
                {
                    entry.InUse = true;
                    list = entry.List;
                    break;
                }
            }

            if (list == null)
            {
                list = new List<IEventHandler>();
                _tmpLists.Add(new TempListEntry { InUse = true, List = list });
            }

            return list;
        }
        private static void ReleaseTempHandlerList(List<IEventHandler> list)
        {
            foreach (var entry in _tmpLists)
            {
                if (entry.List == list)
                {
                    entry.InUse = false;
                    list.Clear();
                    break;
                }
            }
        }
    }
}