﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BitWiseBots.FluentBuilders.Interfaces;
using BitWiseBots.FluentBuilders.Internal;

namespace BitWiseBots.FluentBuilders
{
    /// <summary>
    /// Implements the logic for building an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of object to be built.</typeparam>
    public sealed class Builder<T> : IConstructorBuilder<T> 
    {
        internal readonly BranchNode BuilderRootNode;

        private readonly BuilderRegistrationStore _builderRegistrationStore;
        private readonly Func<IConstructorBuilder<T>, T> _customConstructorFunc;
        private readonly Action<T> _customPostBuildAction;

        private bool _isExecutingConstructorExpression = false;

        internal Builder(BuilderRegistrationStore builderRegistrationStore, Func<IConstructorBuilder<T>,T> customConstructorFunc, Action<T> customPostBuildAction)
        {
            _builderRegistrationStore = builderRegistrationStore;
            _customConstructorFunc = customConstructorFunc;
            _customPostBuildAction = customPostBuildAction;

            BuilderRootNode = new RootNode(this, _builderRegistrationStore, typeof(T) );
        }

        public static implicit operator T(Builder<T> builder)
        {
            return builder.Build();
        }

        /// <summary>
        /// Provides a collection of values to be set on the list property specified by <paramref name="expression"/>
        /// </summary>
        /// <typeparam name="T2">The type of the property being set.</typeparam>
        /// <param name="expression">An expression that accesses the property to be set.</param>
        /// <param name="values">The collection of values to be added on the property.</param>
        public Builder<T> With<T2>(Expression<Func<T, IEnumerable<T2>>> expression, params T2[] values)
        {
            return With(expression, values.ToList());
        }

        /// <summary>
        /// Provides the value to be set on the property specified by <paramref name="expression"/>
        /// </summary>
        /// <typeparam name="T2">The type of the property being set.</typeparam>
        /// <param name="expression">An expression that accesses the property to be set.</param>
        /// <param name="value">The value to be set on the property.</param>
        public Builder<T> With<T2>(Expression<Func<T, T2>> expression, T2 value = default, bool allowDefaults = true)
        {
            var exprStack = ExtractExpressionStack(expression);

            var currentNode = BuilderRootNode;
            while (exprStack.Count > 1)
            {
                var poppedExpr = exprStack.Pop();
                currentNode = currentNode.AddOrGetBranchNode(poppedExpr.Key, poppedExpr.Value);
            }

            var expr = exprStack.Pop();
            currentNode.AddOrGetValueNode<T2>(expr.Key, expr.Value, value, allowDefaults);
            return this;
        }

        /// <summary>
        /// Provides a function to generate a new value to be set on the property specified by <paramref name="expression"/>.
        /// The function will only be executed once <see cref="Build"/> is called.
        /// </summary>
        /// <typeparam name="T2">The type of the property being set.</typeparam>
        /// <param name="expression">An expression that accesses the property to be set.</param>
        /// <param name="valueFunction">A function that returns a value to be set on the property</param>
        public Builder<T> With<T2>(Expression<Func<T, T2>> expression, Func<T2> valueFunction)
        {
            var exprStack = ExtractExpressionStack(expression);

            var currentNode = BuilderRootNode;
            while (exprStack.Count > 1)
            {
                var poppedExpr = exprStack.Pop();
                currentNode = currentNode.AddOrGetBranchNode(poppedExpr.Key, poppedExpr.Value);
            }

            var expr = exprStack.Pop();
            currentNode.AddOrGetValueNode<T2>(expr.Key, expr.Value, valueFunction, false);
            return this;
        }

        /// <summary>
        /// Provides a function to retrieve a value from another call to <see cref="M:With{T2}"/> to be set on the property specified by <paramref name="expression"/>.
        /// The function will only be executed once <see cref="Build"/> is called.
        /// </summary>
        /// <typeparam name="T2">The type of the property being set.</typeparam>
        /// <param name="expression">An expression that accesses the property to be set.</param>
        /// <param name="valueFunction">A function that returns a value to be set on the property</param>
        public Builder<T> With<T2>(Expression<Func<T, T2>> expression, Func<IConstructorBuilder<T>, T2> valueFunction, bool allowDefaults = true)
        {
            var exprStack = ExtractExpressionStack(expression);

            var currentNode = BuilderRootNode;
            while (exprStack.Count > 1)
            {
                var poppedExpr = exprStack.Pop();
                currentNode = currentNode.AddOrGetBranchNode(poppedExpr.Key, poppedExpr.Value);
            }

            var expr = exprStack.Pop();
            currentNode.AddOrGetValueNode<T2>(expr.Key, expr.Value, valueFunction, allowDefaults);
            return this;
        }

        /// <inheritdoc />
        T2 IConstructorBuilder<T>.From<T2>(Expression<Func<T, T2>> expression)
        {
            IConstructorBuilder<T> builder = this;
            return builder.From(expression, default(T2));
        }

        /// <inheritdoc />
        T2 IConstructorBuilder<T>.From<T2>(Expression<Func<T, T2>> expression, Builder<T2> defaultValueBuilder)
        {
            IConstructorBuilder<T> builder = this;
            return builder.From(expression, defaultValueBuilder.Build());
        }

        /// <inheritdoc />
        T2 IConstructorBuilder<T>.From<T2>(Expression<Func<T, T2>> expression, T2 defaultValue)
        {
            var exprStack = ExtractExpressionStack(expression);
            var touchedNodes = new List<TreeNode>();

            TreeNode currentNode = BuilderRootNode;
            while (exprStack.Count > 0)
            {
                var node = exprStack.Pop();
                var branchNode = (BranchNode)currentNode;

                currentNode = branchNode?.Find(node.Key);

                if (currentNode != null)
                {
                    touchedNodes.Add(currentNode);
                }
            }

            object value = defaultValue;
            switch (currentNode)
            {
                case BranchNode targetBranchNode:
                    value = targetBranchNode.ApplyToConstructor();
                    break;
                case ValueNode targetValueNode:
                    value = (T2)targetValueNode.Value;
                    break;
            }

            foreach (var touchedNode in touchedNodes)
            {
                touchedNode.UsedByConstructor = _isExecutingConstructorExpression;
            }

            return (T2)value;
        }

        /// <summary>
        /// Creates a new instance of <typeparamref name="T"/> either using <see cref="Activator"/> or a provided constructor expression.
        /// </summary>
        public T Build()
        {
            var builtObject = Create();

            BuilderRootNode.ApplyTo(builtObject);

            var postBuildAction = _customPostBuildAction ?? _builderRegistrationStore.GetPostBuildAction<T>();
            postBuildAction?.Invoke(builtObject);

            return builtObject;
        }

        /// <summary>
        /// Decomposes the provided <see cref="LambdaExpression"/> into a hierarchy of <see cref="TreeNode"/>.
        /// </summary>
        private static Stack<KeyValuePair<string,Expression>> ExtractExpressionStack(LambdaExpression expression)
        {
            var exprStack = new Stack<KeyValuePair<string, Expression>>();
            var expr = expression.Body;

            // We know we've reached the top of the expression once the type is a ParameterExpression, i.e. (parameter) => ...
            while (!(expr is ParameterExpression))
            {
                var memberExpr = expr.AsMemberExpression();
                if (memberExpr != null)
                {
                    exprStack.Push(new KeyValuePair<string, Expression>(memberExpr.Member.Name, memberExpr));
                    expr = memberExpr.Expression;
                    continue;
                }

                var indexExpr = expr.AsIndexExpression();
                if (indexExpr != null)
                {
                    var indexArgs = indexExpr.GetIndexerArguments();

                    exprStack.Push(new KeyValuePair<string, Expression>($"{indexExpr.Indexer.Name}[{string.Join(",", indexArgs)}]", indexExpr));

                    expr = indexExpr.Object;
                    continue;
                }

                throw new NotSupportedException($"The provided expression contains an expression node type that is not supported: \n\tNode Type:\t{expr.NodeType}\n\tBody:\t\t{expr}.\nEnsure that the expression only contains property accessors.");
            }

            return exprStack;
        }

        /// <summary>
        /// Either executes a <see cref="Func{TConstructorBuilder,T}"/> if one was registered, or attempts to create a <typeparamref name="T"/> instance using <see cref="Activator"/>.
        /// </summary>
        private T Create()
        {
            var constructorExpression = _customConstructorFunc ?? _builderRegistrationStore.GetConstructorFunc<T>();
            if (constructorExpression != null)
            {
                _isExecutingConstructorExpression = true;
                var value = constructorExpression(this);
                _isExecutingConstructorExpression = false;
                return value;
            }

            if (typeof(T).GetConstructor(Type.EmptyTypes) != null)
            {
                return Activator.CreateInstance<T>();
            }

            throw new BuildConfigurationException($"No Parameter-less Constructor present on type {typeof(T).FullName}.\nEnsure a registration exists in an implementation of IBuilderFactoryRegistration.\nAnd that you have called Builders.RunBuilderRegistrationsFromAssemblies with the assembly or assemblies that contain your implementations.");
        }
    }
}