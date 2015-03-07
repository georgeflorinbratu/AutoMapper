using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Linq.Expressions;
using System.Reflection;

namespace Helpers
{
    public static class ReflectionHelper
    {
        #region GetPropertyName

        public static string GetPropertyName<T>(Expression<Func<T, object>> selector)
        {
            return GetPropertyName<T, object>(selector);
        }

        public static string GetPropertyName<T, TProp>(this T instance, Expression<Func<T, TProp>> selector)
        {
            return GetPropertyName<T, TProp>(selector);
        }

        public static string GetPropertyName<T, TProp>(Expression<Func<T, TProp>> selector)
        {
            var bodyString = selector.Body.ToString();
            return bodyString.Substring(bodyString.IndexOf('.') + 1);    
        }
        #endregion

        #region GetActualPropertyName
        /// <summary>
        /// Unlike GetPropertyName, returns the last property name. For x.InvoicesData.PersonIndividual.AddressViewModel.Zip 
        /// it would return Zip, for Cast(x.Zip) it will also return zip
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="selector"></param>
        /// <returns></returns>
        public static string GetLastPropertyName<T>(Expression<Func<T, object>> selector)
        {
            return GetPropertyName<T, object>(selector);
        }

        public static string GetLastPropertyName<T, TProp>(this T instance, Expression<Func<T, TProp>> selector)
        {
            return GetPropertyName<T, TProp>(selector);
        }

        public static string GetLastPropertyName<T, TProp>(Expression<Func<T, TProp>> selector)
        {
            var member = selector.Body as MemberExpression;
            var unary = selector.Body as UnaryExpression;
            var memberInfo = member ?? (unary != null ? unary.Operand as MemberExpression : null);
            if (memberInfo == null)
            {
                // whatever it is that it did before, should actually throw an exception
                var bodyString = selector.Body.ToString();
                return bodyString.Substring(bodyString.IndexOf('.') + 1);
            }
            return memberInfo.Member.Name;

        }
        #endregion

        #region Auto Mapper
        private const string RuntimeExceptionMessageFormat = "A crapat incercand sa faca {0}, iar tipul proprietatii din result este {1}.Vezi inner Exception pentru detalii!";
        private const string ExpressionTreeBuildingException = "A crapat incercand sa descrie atribuirea proprietatii (din Destinatie) numita {0} de tipul {1}. Vezi inner Exception pentru detalii!";
        private readonly static ConstructorInfo NewExceptionConstructorInfo = typeof(Exception).GetConstructor(new Type[] { typeof(string), typeof(Exception) });
        private const string InvalidMemberInitExpression = "Parametrul optional baseMapper este incorect, el trebuie sa fie o expresie lambda al carei body sa fie un constructor cu proprietatile preinitializate. De exemplu: x => new TResult { Id = x.Id, Name = x.Name }";
        /// <summary>
        /// Creeaza o expresie lamda care copie dintr-un obiect Sursa in altul Destinatie toate proprietatile care au acelasi nume si acelasi tip.
        /// Daca crapa in exexcutia expresiei lambda, cel mai probabil crapa una dintre proprietati. Pentru a o identifica apelati cu parametrul userTryCatchForEachProperty = true, si folositi compilarea expresiei lambda cu metoda Compile().
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="baseMapper">este folosit pentru a putea folosi constructori cu parametri si pentru a avea atribuiri custom ale proprietatilor, de exemplu: x => new TResult(param1, param2) { Id = x.Id, Name = x.Name }</param>
        /// <param name="userTryCatchForEachProperty">este folosit pentru debug doar in varianta compilata a expresiei lambda</param>
        /// <returns></returns>
        public static Expression<Func<TSource, TResult>> ExpressionMapper<TSource, TResult>(Expression<Func<TSource, TResult>> baseMapper = null, bool userTryCatchForEachProperty = false, bool useOnlyBaseMapper = false, List<string> excludedProperties = null)
        {
            if (useOnlyBaseMapper)
                return baseMapper;

            excludedProperties = excludedProperties ?? new List<string>();
            
            ParameterExpression parameterX;
            NewExpression newExpression;
            List<MemberBinding> bindings;
            if (baseMapper != null)
            {
                parameterX = baseMapper.Parameters.FirstOrDefault();
                if (baseMapper.Body is MemberInitExpression)
                {
                    var initExpression = baseMapper.Body as MemberInitExpression;
                    newExpression = initExpression.NewExpression;
                    bindings = initExpression.Bindings.Where(x => !excludedProperties.Contains(x.Member.Name)).ToList();
                }
                else if (baseMapper.Body is NewExpression)
                {
                    newExpression = baseMapper.Body as NewExpression;
                    bindings = new List<MemberBinding>();
                }
                else
                {
                    throw new Exception(InvalidMemberInitExpression);
                }
            }
            else
            {
                parameterX = Expression.Parameter(typeof(TSource), "x");
                newExpression = Expression.New(typeof(TResult));
                bindings = new List<MemberBinding>();
            }

            var sourceProperties = typeof(TSource).GetProperties();
            var resultProperties = typeof(TResult).GetProperties();

            foreach (var resultProperty in resultProperties.Where(x => !excludedProperties.Contains(x.Name)))
            {
                try
                {
                    var existingBinding = bindings.FirstOrDefault(x => x.Member.Name == resultProperty.Name);
                    Expression sourceExpression;
                    PropertyInfo sourceProperty;
                    if (existingBinding != null)
                    {
                        var existingMemberAssignment = existingBinding as MemberAssignment;
                        sourceExpression = existingMemberAssignment.Expression;
                        bindings.Remove(existingBinding);
                    }
                    else if ((sourceProperty = sourceProperties.FirstOrDefault(p => p.Name == resultProperty.Name && p.GetType() == resultProperty.GetType())) != null)
                    {
                        sourceExpression = Expression.Property(parameterX, sourceProperty.Name);
                    }
                    else
                        continue;

                    Type t = resultProperty.GetType();
                    var convertedAssignment = t.IsValueType ? Expression.Convert(sourceExpression, resultProperty.PropertyType) : sourceExpression;
                    Expression resultExpression;
                    if (userTryCatchForEachProperty)
                    {
                        var exVariable = Expression.Variable(typeof(Exception), "ex");
                        var exMessageExpression = Expression.Constant(string.Format(RuntimeExceptionMessageFormat, convertedAssignment.ToString(), resultProperty.PropertyType), typeof(string));
                        var throwExpression = Expression.Throw(Expression.New(NewExceptionConstructorInfo, exMessageExpression, exVariable), typeof(Exception));
                        resultExpression = Expression.TryCatch(convertedAssignment, Expression.Catch(exVariable, convertedAssignment));
                    }
                    else
                    {
                        resultExpression = convertedAssignment;
                    }

                    bindings.Add(Expression.Bind(resultProperty, resultExpression));
                }
                catch (Exception ex)
                {
                    throw new Exception(string.Format(ExpressionTreeBuildingException, resultProperty.Name, resultProperty.PropertyType), ex);
                }
            }

            var expressionInit = Expression.MemberInit(newExpression, bindings.ToArray());

            return Expression.Lambda<Func<TSource, TResult>>(expressionInit, parameterX);
        }

        /// <summary>
        /// copie dintr-un obiect Sursa in altul Destinatie toate proprietatile care au acelasi nume si acelasi tip
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="source"></param>
        /// <param name="baseMapper"></param>
        /// <returns></returns>
        public static TResult ExpressionMapperSingle<TSource, TResult>(this TSource source, Expression<Func<TSource, TResult>> baseMapper = null, bool useOnlyBaseMapper = false)
        {
            return ExpressionMapper<TSource, TResult>(baseMapper, true, useOnlyBaseMapper).Compile()(source);
        }

        /// <summary>
        /// copie dintr-un obiect Sursa in altul Destinatie toate proprietatile care au acelasi nume si acelasi tip
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="source"></param>
        /// <param name="baseMapper"></param>
        /// <returns></returns>
        public static void ExpressionMapperSingle<TSource, TResult>(this TSource source, out TResult destination, Expression<Func<TSource, TResult>> baseMapper = null, bool useOnlyBaseMapper = false)
        {
            destination = ExpressionMapper<TSource, TResult>(baseMapper, true, useOnlyBaseMapper).Compile()(source);
        }

        public static IEnumerable<TResult> SelectExpressionMapper<TSource, TResult>(this IEnumerable<TSource> source, Expression<Func<TSource, TResult>> baseMapper = null)
        {
            var mapper = ExpressionMapper<TSource, TResult>(baseMapper, true).Compile();
            return source.Select(mapper);
        }

        public static IQueryable<TResult> SelectExpressionMapper<TSource, TResult>(this IQueryable<TSource> source, Expression<Func<TSource, TResult>> baseMapper = null, bool userTryCatchForEachProperty = false, bool useOnlyBaseMapper = false)
        {
            var mapper = ExpressionMapper<TSource, TResult>(baseMapper, userTryCatchForEachProperty, useOnlyBaseMapper);
            return source.Select(mapper);
        }
        #endregion

		#region CopyProperties
        /// <summary>
        /// type safely, attempts to copy all properties found in both objects
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        public static void CopyPropertiesTo<T1, T2>(this T1 source, T2 destination, params string[] skippedProperties)
        {
            var sourceType = typeof(T1);
            var destinationType = typeof(T2);
            
            foreach (PropertyInfo p1 in sourceType.GetProperties().Where(x => !skippedProperties.Contains(x.Name)))
            {
                PropertyInfo p2 = destinationType.GetProperty(p1.Name);
                if (p2 != null)
                {
                    p2.SetValue(destination, p1.GetValue(source, null), null);
                }
            }
        }

        /// <summary>
        /// using instances types, attempts to copy all properties found in both objects
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        public static void CopyPropertiesOfInstanceTypesTo<T1, T2>(this T1 source, T2 destination, params string[] skippedProperties)
        {
            var sourceType = source.GetType();
            var destinationType = destination.GetType();

            foreach (PropertyInfo p1 in sourceType.GetProperties().Where(x => !skippedProperties.Contains(x.Name)))
            {
                PropertyInfo p2 = destinationType.GetProperty(p1.Name);
                if (p2 != null)
                {
                    p2.SetValue(destination, p1.GetValue(source, null), null);
                }
            }
        }
		#endregion
    }
}
