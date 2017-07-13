﻿using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using AutoQueryable.Extensions;
using AutoQueryable.Models;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;

namespace AutoQueryable.Helpers
{
    internal static class RuntimeTypeBuilder
    {
        private static readonly ModuleBuilder moduleBuilder;
        private static readonly IDictionary<string, Type> builtTypes;

        static RuntimeTypeBuilder()
        {
            AssemblyBuilder dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("AutoQueryableDynamicAssembly"), AssemblyBuilderAccess.Run);
            moduleBuilder = dynamicAssembly.DefineDynamicModule("AutoQueryableDynamicAssemblyModule");

            builtTypes = new Dictionary<string, Type>();
        }

        internal static Type GetRuntimeType<TEntity>(IDictionary<string, object> fields)
        {
            string typeKey = GetTypeKey<TEntity>(fields);
            if (!builtTypes.ContainsKey(typeKey))
            {
                lock (moduleBuilder)
                {
                    //double check 
                    if (!builtTypes.ContainsKey(typeKey))
                    {
                        builtTypes[typeKey] = GetRuntimeType(typeKey, fields);
                    }
                }
            }

            return builtTypes[typeKey];
        }

        internal static Type GetRuntimeType(string typeName, IEnumerable<KeyValuePair<string, object>> properties)
        {
            var typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Serializable);
            foreach (var property in properties)
            {
                if (property.Value is PropertyInfo)
                {
                    typeBuilder.AddProperty(property.Key, property.Value as PropertyInfo);
                }
                else
                {
                    typeBuilder.AddProperty(property.Key, property.Value as Type);

                }
            }

            return typeBuilder.CreateTypeInfo().AsType();
        }

        private static string GetTypeKey<TEntity>(IEnumerable<KeyValuePair<string, object>> fields)
        {

            var fieldsKey = fields.Aggregate(string.Empty, (current, field) =>
            {
                if (field.Value is PropertyInfo)
                {
                    current = current + (field.Key + "#" + (field.Value as PropertyInfo).Name + "#");
                }
                else
                {
                    current = current + (field.Key + "#" + (field.Value as Type).FullName + "#");
                }
                return current;
            });
            return  typeof(TEntity).FullName + getHash(fieldsKey);
        }
        private static string getHash(string text)
        {
            // SHA512 is disposable by inheritance.  
            using (var sha256 = SHA256.Create())
            {
                // Send a sample text to hash.  
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
                // Get the hashed string.  
                return BitConverter.ToString(hashedBytes).Replace("-", "").ToLower();
            }
        }
    }

    public static class SelectHelper
    {
        public static Expression<Func<TEntity, object>> GetSelector<TEntity>(IEnumerable<SelectColumn> columns)
        {
            Dictionary<string, Expression> memberExpressions = new Dictionary<string, Expression>();

            ParameterExpression parameter = Expression.Parameter(typeof(TEntity), "p");

            MemberInitExpression memberInit =  InitType<TEntity>(columns, parameter);
            return Expression.Lambda<Func<TEntity, object>>(memberInit, parameter);

        }

        private static Expression GetMemberExpression<TEntity>(Expression parent, SelectColumn column, bool isLambdaBody = false)
        {
            bool isCollection = parent.Type.IsEnumerableButNotString();
            // If the current column has no sub column, return the final property.
            if (!column.HasSubColumn && !isCollection)
            {
                if (!parent.Type.PropertyExist(column.Name))
                {
                    return null;
                }
                return Expression.PropertyOrField(parent, column.Name);
            }

            Expression nextParent = parent;
            // If we are not inside a select lambda, the next parent will be the current column
            if (!isLambdaBody)
            {
                if (!parent.Type.PropertyExist(column.Name))
                {
                    return null;
                }
                var ex = Expression.PropertyOrField(parent, column.Name);
                // Current column is a collection, let's create a select lambda, eg: SalesOrderDetail.Select(x => x.LineTotal)
                if (ex.Type.IsEnumerableButNotString())
                {
                    ParameterExpression param = ex.CreateParameterFromGenericType();
                    Expression lambdaBody = GetMemberExpression<TEntity>(param, column, true);
                    return ex.CreateSelect(lambdaBody, param);
                }
                else
                {
                    nextParent = Expression.PropertyOrField(parent, column.Name);
                }
            }

            return InitType<TEntity>(column.SubColumns, nextParent);
        }

        public static IEnumerable<SelectColumn> GetSelectableColumns(Clause selectClause, string[] unselectableProperties, Type entityType)
        {

            if (selectClause == null)
            {
                // TODO unselectable properties.
                //return GetSelectableColumns(unselectableProperties, entityType);
                return new List<SelectColumn>();
            }
            ICollection<SelectColumn> allSelectColumns = new List<SelectColumn>();
            ICollection<SelectColumn> selectColumns = new List<SelectColumn>();
            var selection = selectClause.Value.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
            var selectionWithColumnPath = new List<string[]>();
            foreach (string selectionItem in selection)
            {
                var columnPath = selectionItem.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
                selectionWithColumnPath.Add(columnPath);

            }
            foreach (string[] selectionColumnPath in selectionWithColumnPath)
            {
                var parentType = entityType;
                for (int i = 0; i < selectionColumnPath.Length; i++)
                {
                    string key = string.Join(".", selectionColumnPath.Take(i + 1)).ToLowerInvariant();

                    var columnName = selectionColumnPath[i];
                    var property = parentType.GetProperties().FirstOrDefault(x => x.Name.ToLowerInvariant() == columnName.ToLowerInvariant());
                    if (property == null)
                    {
                        break;
                    }
                    bool isCollection = property.PropertyType.IsEnumerable();
                    if (isCollection)
                    {
                        parentType = property.PropertyType.GetGenericArguments().FirstOrDefault();
                    }
                    else
                    {
                        parentType = property.PropertyType;
                    }
                    SelectColumn column = allSelectColumns.FirstOrDefault(all => all.Key == key);
                    if (column == null)
                    {
                        if (unselectableProperties != null && unselectableProperties.Contains(key, StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        column = new SelectColumn
                        {
                            Key = key,
                            Name = columnName,
                            SubColumns = new List<SelectColumn>(),
                            Type = property.PropertyType
                        };
                        allSelectColumns.Add(column);
                        if (i == 0)
                        {
                            selectColumns.Add(column);
                        }
                        else
                        {
                            string parentKey = string.Join(".", selectionColumnPath.Take(i)).ToLowerInvariant();
                            SelectColumn parentColumn = allSelectColumns.FirstOrDefault(all => all.Key == parentKey);
                            if (selection.Contains(parentKey + ".*", StringComparer.OrdinalIgnoreCase))
                            {
                                parentColumn.InclusionType = SelectInclusingType.IncludeAllProperties;
                            }
                            else if (selection.Contains(parentKey, StringComparer.OrdinalIgnoreCase))
                            {
                                parentColumn.InclusionType = SelectInclusingType.IncludeBaseProperties;
                            }

                            column.ParentColumn = parentColumn;
                            parentColumn.SubColumns.Add(column);
                        }
                    }
                }
            }
            foreach (var selectColumn in selectColumns)
            {
                if (selectColumn.SubColumns.Any() && selectColumn.InclusionType != SelectInclusingType.Default)
                {
                    var selectableColumns = GetSelectableColumns(unselectableProperties, selectColumn.Type, selectColumn.InclusionType);
                    foreach (var columnName in selectableColumns)
                    {
                        if (!selectColumn.SubColumns.Any(x => x.Name.ToLowerInvariant() == columnName.ToLowerInvariant()))
                        {
                            var column = new SelectColumn
                            {
                                Key = selectColumn.Key + "." + columnName,
                                Name = columnName,
                                SubColumns = new List<SelectColumn>(),
                                Type = selectColumn.Type.GetProperties().Single(x => x.Name == columnName).PropertyType,
                                ParentColumn = selectColumn
                            };
                            selectColumn.SubColumns.Add(column);
                        }

                    }
                }

            }

            return selectColumns;
        }

        public static IEnumerable<string> GetSelectableColumns(string[] unselectableProperties, Type entityType, SelectInclusingType selectInclusingType = SelectInclusingType.IncludeBaseProperties)
        {
            IEnumerable<string> columns = null;
            if (selectInclusingType == SelectInclusingType.IncludeBaseProperties)
            {
                columns = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p =>
                    (p.PropertyType.GetTypeInfo().IsGenericType && p.PropertyType.GetTypeInfo().GetGenericTypeDefinition() == typeof(Nullable<>))
                    || (!p.PropertyType.GetTypeInfo().IsClass && !p.PropertyType.GetTypeInfo().IsGenericType)
                    || p.PropertyType.GetTypeInfo().IsArray
                    || p.PropertyType == typeof(string)
                    )
                    .Select(p => p.Name);
            }
            else if (selectInclusingType == SelectInclusingType.IncludeAllProperties)
            {
                columns = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                   .Select(p => p.Name);
            }

            if (unselectableProperties != null)
            {
                columns = columns?.Where(c => !unselectableProperties.Contains(c, StringComparer.OrdinalIgnoreCase));
            }

            return columns?.ToList();
        }

        private static Dictionary<string, object> GetTypeProperties(Dictionary<string, Expression> expressions)
        {
            return expressions.ToDictionary(x => x.Key, x =>
            {
                if (x.Value is MemberExpression)
                {
                    return (x.Value as MemberExpression).Member as object;
                }
                if (x.Value is MemberInitExpression)
                {
                    return x.Value.Type;
                }
                if (x.Value is MethodCallExpression)
                {
                    return x.Value.Type;
                }
                return null;
            });
        }
        
        public static MemberInitExpression InitType<TEntity>(IEnumerable<SelectColumn> columns, Expression node)
        {
            var expressions = new Dictionary<string, Expression>();
            foreach (SelectColumn subColumn in columns)
            {
                Expression ex = GetMemberExpression<TEntity>(node, subColumn);
                expressions.Add(subColumn.Name, ex);
            }

            var properties = GetTypeProperties(expressions);

            Type dynamicType = RuntimeTypeBuilder.GetRuntimeType<TEntity>(properties);
            NewExpression ctor = Expression.New(dynamicType);
            return Expression.MemberInit(ctor, expressions.Select(p => Expression.Bind(dynamicType.GetProperty(p.Key), p.Value)));
        }
    }
}