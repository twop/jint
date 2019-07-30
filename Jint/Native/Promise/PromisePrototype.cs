﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Jint.Collections;
using Jint.Native.Function;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;

namespace Jint.Native.Promise
{
    public sealed class PromisePrototype : ObjectInstance
    {
        private PromiseConstructor _promiseConstructor;

        private PromisePrototype(Engine engine) : base(engine)
        {
        }

        public static PromisePrototype CreatePrototypeObject(Engine engine, PromiseConstructor promiseConstructor)
        {
            var obj = new PromisePrototype(engine)
            {
                _prototype = engine.Object.PrototypeObject,
                _promiseConstructor = promiseConstructor
            };

            return obj;
        }

        protected override void Initialize()
        {
            var properties = new PropertyDictionary(15, checkExistingKeys: false)
            {
                ["constructor"] = new PropertyDescriptor(_promiseConstructor, PropertyFlag.NonEnumerable),
                ["then"] = new PropertyDescriptor(new ClrFunctionInstance(Engine, "then", Then, 1, PropertyFlag.Configurable), true, false, true),
                ["catch"] = new PropertyDescriptor(new ClrFunctionInstance(Engine, "catch", Catch, 1, PropertyFlag.Configurable), true, false, true)
            };
            SetProperties(properties);
        }
        
        public JsValue Then(JsValue thisValue, JsValue[] args)
        {
            if (!(thisValue is PromiseInstance promise))
                throw ExceptionHelper.ThrowTypeError(_engine, "Method Promise.prototype.then called on incompatible receiver");

            var chainedPromise = new PromiseInstance(Engine, promise)
            {
                _prototype = _promiseConstructor.PrototypeObject
            };

            var resolvedCallback = (args.Length >= 1 ? args[0] : null) as FunctionInstance ?? Undefined;
            var rejectedCallback = (args.Length >= 2 ? args[1] : null) as FunctionInstance ?? Undefined;

            promise.Task.ContinueWith(t =>
            {
                var continuation = (Action) (() => { });

                if (t.Status == TaskStatus.RanToCompletion)
                {
                    if (resolvedCallback == Undefined)
                    {
                        continuation = () =>
                        {
                            //  If no success callback then simply pass the return value to the next promise in chain
                            chainedPromise.Resolve(null, new[] { t.Result });
                        };
                    }
                    else
                    {
                        continuation = () =>
                        {
                            var result = resolvedCallback.Invoke(t.Result);
                            chainedPromise.Resolve(null, new[] {result});
                        };
                    }

                }
                

                else if (t.IsFaulted || t.IsCanceled)
                {
                    var rejectValue = Undefined;

                    if (t.Exception?.InnerExceptions.FirstOrDefault() is PromiseRejectedException promiseRejection)
                        rejectValue = promiseRejection.RejectedValue;

                    if (rejectedCallback == Undefined)
                    {
                        continuation = () =>
                        {
                            //  If no error callback then simply pass the error value to the next promise in chain
                            chainedPromise.Reject(null, new[] { rejectValue });
                        };
                    }
                    else
                    {
                        continuation = () =>
                        {
                            var result = rejectedCallback.Invoke(rejectValue);

                            //todo - if the above throws or returns a promise which is rejected then reject the chained promise too

                            //  Else chain is restored
                            chainedPromise.Resolve(Undefined, new[] {Undefined});
                        };
                    }
                }

                _engine.QueuePromiseContinuation(continuation);
            });

            return chainedPromise;
        }

        public JsValue Catch(JsValue thisValue, JsValue[] args) => Then(thisValue, new [] {Undefined, args.Length >= 1 ? args[0] : Undefined});

    }
}