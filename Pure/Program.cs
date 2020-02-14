using System;
using static Pure.PureCSharp;
using System.Linq.Expressions;
using System.Linq;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace Pure
{
    public static class PureCSharp
    {
        /// <summary>
        /// Runs mapping function A->B over IEnumerable<A>, and returns List<B>
        /// </summary>
        /// <typeparam name="A">Original item source type</typeparam>
        /// <typeparam name="B">Return list type</typeparam>
        /// <param name="enumerable">Enumerable to map over</param>
        /// <param name="f">Function A -> B</param>
        /// <returns>List<B></returns>
        public static List<B> Map<A,B>(IEnumerable<A> enumerable, Func<A,B> f)
        {
            return enumerable.Aggregate<A, List<B>>(new List<B>(), (list, expr) =>
            {
                list.Add((B)f(expr));
                return list;
            });
        }

        private static ConcurrentDictionary<object, ConcurrentDictionary<object, object>> cache = new ConcurrentDictionary<object, ConcurrentDictionary<object, object>>();

        /// <summary>
        /// Resets the pure function output cache.
        /// </summary>
        public static void ResetCache()
        {
            lock (cache)
            {
                cache = new ConcurrentDictionary<object, ConcurrentDictionary<object, object>>();
            }
        }

        /// <summary>
        /// Checks if a function's response to a given input is cached. If so, returns it, else runs the function and caches the output.
        /// </summary>
        /// <param name="ps">A Tuple where Item1 is a function and Item2 is a parameter</param>
        /// <returns>The result of the function call</returns>
        private static object CallFunction(Tuple<object,object> ps)
        {
            if (ps.Item1 != null && ps.Item2 != null)
            {
                
                if (cache.ContainsKey(ps.Item1))
                {
                    if (cache[ps.Item1].ContainsKey(ps.Item2))
                    {
                        return cache[ps.Item1][ps.Item2];
                    }
                }
                else
                {
                    lock (cache)
                    {
                        cache.TryAdd(ps.Item1, new ConcurrentDictionary<object, object>());
                    }
                }
                var func = ps.Item1;
                object returnValue = ((dynamic)func)((dynamic)ps.Item2);
                lock (cache[func]) { 
                    cache[func].TryAdd(ps.Item2, returnValue);
                    return returnValue;
                }

            }
            else
            {
                var func = ps.Item1;
                object returnValue = ((dynamic)func)((dynamic)ps.Item2);
                return returnValue;
            }
        }

        private static bool CanLambdaBeThreaded(LambdaExpression ex)
        {
            var q = from z in ex.Parameters
                    select z.Type.IsValueType;
            return q.Aggregate<bool, bool>(true, (x, y) => x & y) && ex.ReturnType.IsValueType;
        }

        private static bool CanInvokeBeThreaded(InvocationExpression ex)
        {
            var q = from z in ex.Arguments
                    select z.Type.IsValueType;
            return q.Aggregate<bool, bool>(true, (x, y) => x & y) && ex.Type.IsValueType;
        }

        /// <summary>
        /// Checks if both sides of a binary expression can (likely) be threaded.
        /// </summary>
        /// <param name="ex">The binary expression to check.</param>
        /// <returns></returns>
        private static bool CanBinaryExpressionBeThreaded(BinaryExpression ex)
        {
            bool threadableLeft = false;
            bool threadableRight = false;
            switch (ex.NodeType)
            {
                case ExpressionType.Add:
                case ExpressionType.AddChecked:
                case ExpressionType.Subtract:
                case ExpressionType.SubtractChecked:
                case ExpressionType.Multiply:
                case ExpressionType.MultiplyChecked:
                case ExpressionType.Divide:
                case ExpressionType.And:
                case ExpressionType.AndAlso:
                case ExpressionType.Or:
                case ExpressionType.OrElse:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.Equal:
                case ExpressionType.ExclusiveOr:
                case ExpressionType.NotEqual:
                    break;
                default:
                    return false;

            }

            switch (ex.Left)
            {
                case BinaryExpression e:
                    threadableLeft |= CanBinaryExpressionBeThreaded(e);
                    break;
                case LambdaExpression e:
                    threadableLeft |= CanLambdaBeThreaded(e);
                    break;
                case InvocationExpression e:
                    threadableLeft |= CanInvokeBeThreaded(e);
                    break;
            }

            switch (ex.Right)
            {
                case BinaryExpression e:
                    threadableRight |= CanBinaryExpressionBeThreaded(e);
                    break;
                case LambdaExpression e:
                    threadableRight |= CanLambdaBeThreaded(e);
                    break;
                case InvocationExpression e:
                    threadableRight |= CanInvokeBeThreaded(e);
                    break;
            }

            return threadableLeft && threadableRight;
        }

        class ImpureFunctionException : Exception
        {
            public ImpureFunctionException(string message) : base(message)
            {

            }
        }

        public static bool AllowFunctionsWithoutCachingToBeImpure = false;

        /// <summary>
        /// Explore an expression tree, and make all the necessary changes to make it recursive, and add output caching and multithreading
        /// </summary>
        /// <param name="replace">The expression that constitutes a call to the stub that we pass in to the function to allow recursion</param>
        /// <param name="with">The expression that constitutes a call to the variable we declare to represent our expression as a function</param>
        /// <param name="caching">Insert caching code?</param>
        /// <param name="multithreading">Insert multithreading code?</param>
        /// <param name="pureFunction">True to check for field accesses, etc</param>
        /// <returns>Returns a recursive function that takes a parameter that represents the current item in the expression tree.</returns>
        private static Func<Expression, Expression> ReplaceInstancesOfExpressionFactory(Expression replace, Expression with, bool caching = false, bool multithreading = false, bool pureFunction = false)
        {
            //used so that we can escape the world of Expression<>
            Expression<Func<Tuple<object,object>, object>> c = (f) => CallFunction(f);

            //operation, and two actions
            Func<ExpressionType, Func<object>, Func<object>, object> newThread = (t, left, right) =>
            {
                int avail;
                int complete;
                System.Threading.ThreadPool.GetAvailableThreads(out avail, out complete);
                int max;
                ThreadPool.GetMaxThreads(out max,out complete);

                dynamic leftResponse = null;
                dynamic rightResponse = null;
                if (avail > 2)
                {
                    Task t1 = new Task(()=> { 
                        leftResponse = left();
                    });
                    Task t2 = new Task(() => { 
                        rightResponse = right();
                    });

                    t2.Start();
                    t1.RunSynchronously();
                    t2.Wait();
                    
                }
                else
                {
                    leftResponse = left();
                    rightResponse = right();
                }

                switch (t)
                {
                    case ExpressionType.Add:
                        unchecked
                        {
                            return leftResponse + rightResponse;
                        }
                    case ExpressionType.AddChecked:
                        return leftResponse + rightResponse;
                    case ExpressionType.Subtract:
                        unchecked
                        {
                            return leftResponse - rightResponse;
                        }
                    case ExpressionType.SubtractChecked:
                        return leftResponse - rightResponse;
                    case ExpressionType.Multiply:
                        unchecked
                        {
                            return leftResponse * rightResponse;
                        }
                    case ExpressionType.MultiplyChecked:
                        return leftResponse * rightResponse;
                    case ExpressionType.Divide:
                        return leftResponse / rightResponse;
                    case ExpressionType.And:
                        return leftResponse & rightResponse;
                    case ExpressionType.AndAlso:
                        return leftResponse && rightResponse;
                    case ExpressionType.Or:
                        return leftResponse | rightResponse;
                    case ExpressionType.OrElse:
                        return leftResponse || rightResponse;
                    case ExpressionType.LessThan:
                        return leftResponse < rightResponse;
                    case ExpressionType.LessThanOrEqual:
                        return leftResponse <= rightResponse;
                    case ExpressionType.GreaterThan:
                        return leftResponse > rightResponse;
                    case ExpressionType.GreaterThanOrEqual:
                        return leftResponse >= rightResponse;
                    case ExpressionType.Equal:
                        return leftResponse == rightResponse;
                    case ExpressionType.NotEqual:
                        return leftResponse != rightResponse;
                    case ExpressionType.ExclusiveOr:
                        return leftResponse ^ rightResponse;
                    default:
                        throw new InvalidOperationException(t.ToString() + " is not a supported when running Parallel!");
                }

            };
            
            //once again exists to allow us to escape needing everything to be an expression.
            Expression<Func<ExpressionType, Func<object>, Func<object>, object>> newT = (t, left, right) => newThread(t, left, right);
            
            Func<dynamic, dynamic, dynamic> call = (f, x) => f(x);

            //turn Func<object,object> into Func<object>, so that we don't need to worry about the arguments anymore
            Expression<Func<object, object, Func<object>>> wrap = (f, x) => new Func<object>(() => call(f,x));

            Expression<Func<object, object, Tuple<object, object>>> makeTuple = (x, y) => new Tuple<object, object>(x, y);

            //this needs to be here so we can have a recursive Func
            Func<Expression, Expression> RIOE = null;

            //write the bulk of the code in here, so that we don't need to give parameters every time we recurse.
            RIOE = (e) =>
            {
                if (e.Equals(replace))
                    return with;
                switch (e)
                {
                    case UnaryExpression ex:
                        return Expression.MakeUnary(ex.NodeType, RIOE(ex.Operand), ex.Type, ex.Method);
                    case InvocationExpression ex:
                        if (caching && ex.Arguments.Count == 1)
                        {
                            var arg = Map(ex.Arguments, Compose(RIOE, (expr) => Expression.Convert(expr, typeof(object)))).First();
                            var func = Expression.Convert(RIOE(ex.Expression), typeof(object));
                            
                            return Expression.Convert(Expression.Invoke(c,Expression.Invoke(makeTuple, func, arg)),ex.Type);
                        }
                        else
                        {
                            return Expression.Invoke(RIOE(ex.Expression), Map(ex.Arguments, RIOE));
                        }
                    case BinaryExpression ex:
                        if (multithreading && CanBinaryExpressionBeThreaded(ex) && ex.Left is InvocationExpression && ex.Right is InvocationExpression)
                        {
                            var exTypeArg = Expression.Constant(ex.NodeType, typeof(ExpressionType));
                            var lexpression = RIOE(ex.Left);
                            if (lexpression is UnaryExpression && lexpression.NodeType == ExpressionType.Convert)
                                lexpression = ((UnaryExpression)lexpression).Operand;
                            var leftInvokeBase = (InvocationExpression)(lexpression);
                            var rexpression = RIOE(ex.Right);
                            if (rexpression is UnaryExpression && rexpression.NodeType == ExpressionType.Convert)
                                rexpression = ((UnaryExpression)rexpression).Operand;
                            var rightInvokeBase = (InvocationExpression)(rexpression);
                            var lWrapped = Expression.Invoke(wrap,leftInvokeBase.Expression,Expression.Convert(leftInvokeBase.Arguments.First(),typeof(object)));
                            var rWrapped = Expression.Invoke(wrap, rightInvokeBase.Expression, Expression.Convert(rightInvokeBase.Arguments.First(), typeof(object)));
                            return Expression.Convert(Expression.Invoke(newT,Expression.Constant(ex.NodeType,typeof(ExpressionType)),lWrapped,rWrapped),ex.Type);
                        }
                        return Expression.MakeBinary(ex.NodeType, RIOE(ex.Left), RIOE(ex.Right));
                    case DynamicExpression ex:
                        return Expression.MakeDynamic(ex.DelegateType, ex.Binder, Map(ex.Arguments,RIOE));
                    case BlockExpression ex:
                        return Expression.Block(Map(ex.Expressions,RIOE));
                    case MethodCallExpression ex:
                        return Expression.Call(RIOE(ex.Object), ex.Method, Map(ex.Arguments,RIOE));
                    case ConditionalExpression ex:
                        return Expression.Condition(RIOE(ex.Test),RIOE(ex.IfTrue),RIOE(ex.IfFalse));
                    case ConstantExpression ex:
                        return ex;
                    case LoopExpression ex:
                        return Expression.Loop(RIOE(ex.Body));
                    case GotoExpression ex:
                        return Expression.MakeGoto(ex.Kind, ex.Target, RIOE(ex.Value), ex.Type);
                    case MemberExpression ex:
                        if (pureFunction && !ex.Expression.Type.IsValueType && (!AllowFunctionsWithoutCachingToBeImpure || caching))
                        {
                            Type t = ex.Type;
                            if (!t.IsGenericType)
                            {
                                throw new ImpureFunctionException("Can't access members of a class that are not of type PureFunc");
                            }
                            Type gtd = t.GetGenericTypeDefinition();
                            if (!t.DoesTypeMatchGenericType(typeof(PureFunc<,>)) && !t.DoesTypeMatchGenericType(typeof(PureFunc<>)))
                            {
                                throw new ImpureFunctionException("Can't access members of a class that are not of type PureFunc");
                            }
                        }
                        
                        return Expression.MakeMemberAccess(RIOE(ex.Expression), ex.Member);
                    case LabelExpression ex:
                        return Expression.Label(ex.Target, RIOE(ex.DefaultValue));
                    case LambdaExpression ex:
                        return Expression.Lambda(RIOE(ex.Body), parameters: Map(ex.Parameters, Compose(RIOE,CastTo<Expression,ParameterExpression>)));
                    case ParameterExpression ex:
                        return ex;
                    default:
                        throw new InvalidOperationException();

                }
            };
            return RIOE;
        }

        /// <summary>
        /// Check if a type matches a given generic type
        /// </summary>
        /// <param name="t"></param>
        /// <param name="to"></param>
        /// <returns>true if the type is or inherrited from the generic type</returns>
        static bool DoesTypeMatchGenericType(this Type t, Type to)
        {
            Type type = t;
            while (type != null)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == to)
                    return true;
                type = type.BaseType;
            }
            return false;
        }

        public delegate TResult PureFunc<TResult>();
        public delegate TResult PureFunc<T, TResult>(T a);

        /// <summary>
        /// Produces a pure function with optimizations from the given lambda
        /// </summary>
        /// <typeparam name="T">Function parameter type</typeparam>
        /// <typeparam name="TResult">Function return type</typeparam>
        /// <param name="ex">The function.
        /// Note: The lambda will usually take the form of (funcName) => (parameter) => body, so that funcName(arg) can be called for self-recursion.</param>
        /// <param name="caching">Enable result caching</param>
        /// <param name="threaded">Enable multithreading</param>
        /// <returns>Optimized pure function.</returns>
        public static PureFunc<T,TResult> PureFunction<T, TResult>(Expression<Func<Func<T,TResult>, Func<T,TResult>>> ex, bool caching = true, bool threaded = true)
        {
            var methodVar = Expression.Variable(typeof(Func<T, TResult>), ex.Parameters[0].Name);
            var paramVar = ((LambdaExpression)ex.Body).Parameters[0];

            //Expression<PureFunc<Func<T,TResult>,PureFunc<T, TResult>>> makePure = (a) => new PureFunc<T, TResult>(a);

            var resultingExpression = Expression.Lambda(Expression.Block(
                    new[] { methodVar },
                    Expression.Assign(methodVar,
                        ReplaceInstancesOfExpressionFactory(ex.Parameters[0], methodVar, caching, threaded, true)(ex.Body)
                    ),
                    Expression.Invoke(methodVar, paramVar)
                ),
                paramVar);

            while (resultingExpression.CanReduce)
            {
                resultingExpression = (LambdaExpression)resultingExpression.Reduce();
            }

            var compiled = resultingExpression.Compile();

            return new PureFunc<T, TResult>((Func<T, TResult>)compiled);
        }

        /// <summary>
        /// Make a pure function that returns a constant value
        /// </summary>
        /// <typeparam name="T">The type of the constant</typeparam>
        /// <param name="expression">The constant value</param>
        /// <returns></returns>
        public static PureFunc<T> PureFunction<T>(Expression<Func<T>> expression)
        {
            T returnVal = expression.Compile()();
            return new PureFunc<T>(() => returnVal);
        }

        /// <summary>
        /// Invoke a curried function
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="f"></param>
        /// <param name="param"></param>
        /// <returns>The result</returns>
        public static TResult FI<T,TResult>(this PureFunc<T,TResult> f, T param)
        {
            return f(param);
        }

        /// <summary>
        /// Invoke a curried function
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="f"></param>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns>The result</returns>
        public static TResult FI<T1,T2,TResult>(this PureFunc<T1,Func<T2,TResult>> f, T1 a, T2 b)
        {
            return f(a)(b);
        }

        /// <summary>
        /// Invoke a curried function
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="f"></param>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="c"></param>
        /// <returns>The result</returns>
        public static TResult FI<T1, T2, T3, TResult>(this PureFunc<T1, Func<T2, Func<T3, TResult>>> f, T1 a, T2 b, T3 c)
        {
            return f(a)(b)(c);
        }

        /// <summary>
        /// Invoke a curried function
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <typeparam name="T4"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="f"></param>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="c"></param>
        /// <param name="d"></param>
        /// <returns>The result</returns>
        public static TResult FI<T1, T2, T3, T4, TResult>(this PureFunc<T1, Func<T2, Func<T3, Func<T4,TResult>>>> f, T1 a, T2 b, T3 c, T4 d)
        {
            return f(a)(b)(c)(d);
        }

        static B CastTo<A, B>(A itm)
        {
            return (B)(object)itm;
        }

        /// <summary>
        /// Compose two functions A -> B, B -> C, into one function A -> C
        /// </summary>
        /// <typeparam name="A"></typeparam>
        /// <typeparam name="B"></typeparam>
        /// <typeparam name="C"></typeparam>
        /// <param name="a">First function (A -> B)</param>
        /// <param name="b">Second function (B -> C)</param>
        /// <returns></returns>
        public static Func<A,C> Compose<A, B, C>(Func<A,B> a, Func<B,C> b)
        {
            return new Func<A, C>((v) => b(a(v)));
        }

        /// <summary>
        /// Create a recursive lambda.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="ex">The expression for the Lambda.
        /// Note: The lambda will usually take the form of (funcName) => (parameter) => body, so that funcName(arg) can be called for self-recursion.</param>
        /// <returns></returns>
        public static Func<T,TResult> RecursiveLambda<T, TResult>(Expression<Func<Func<T, TResult>, Func<T, TResult>>> ex)
        {
            var methodVar = Expression.Variable(typeof(Func<T, TResult>), ex.Parameters[0].Name);
            var paramVar = ((LambdaExpression)ex.Body).Parameters[0];

            var resultingExpression = Expression.Lambda(Expression.Block(
                    new[] { methodVar },
                    Expression.Assign(methodVar,
                        ReplaceInstancesOfExpressionFactory(ex.Parameters[0], methodVar)(ex.Body)
                    ),
                    Expression.Invoke(methodVar, paramVar)
                ),
                paramVar);

            while (resultingExpression.CanReduce)
            {
                resultingExpression = (LambdaExpression)resultingExpression.Reduce();
            }

            var compiled = resultingExpression.Compile();


            return (Func<T, TResult>)compiled;
        }
    }
}
