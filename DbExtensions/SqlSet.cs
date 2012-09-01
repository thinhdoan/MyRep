﻿// Copyright 2012 Max Toro Q.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DbExtensions {

   [DebuggerDisplay("{definingQuery}")]
   public class SqlSet : ISqlSet<SqlSet, object> {

      readonly DbConnection connection;
      readonly SqlBuilder definingQuery;
      readonly Type resultType;
      readonly int setIndex = 1;

      public DbConnection Connection { get { return connection; } }
      protected internal TextWriter Log { get; set; }

      public SqlSet(DbConnection connection, SqlBuilder definingQuery) 
         : this(connection, definingQuery, adoptQuery: false) { }

      public SqlSet(DbConnection connection, SqlBuilder definingQuery, Type resultType) 
         : this(connection, definingQuery, resultType, adoptQuery: false) { }

      protected SqlSet(DbConnection connection, SqlBuilder definingQuery, bool adoptQuery) {

         if (connection == null) throw new ArgumentNullException("connection");
         if (definingQuery == null) throw new ArgumentNullException("definingQuery");

         this.connection = connection;
         this.definingQuery = (adoptQuery) ?
            definingQuery
            : definingQuery.Clone();
      }

      protected SqlSet(DbConnection connection, SqlBuilder definingQuery, Type resultType, bool adoptQuery) 
         : this(connection, definingQuery, adoptQuery) {
         
         this.resultType = resultType;
      }

      protected SqlSet(SqlSet set, SqlBuilder superQuery) {

         if (set == null) throw new ArgumentNullException("set");

         if (superQuery == null
            || Object.ReferenceEquals(superQuery, set.definingQuery)) {
            superQuery = set.GetDefiningQuery();
         }

         this.connection = set.connection;
         this.definingQuery = superQuery;
         this.resultType = set.resultType;
         this.Log = set.Log;
         this.setIndex += set.setIndex;
      }

      protected SqlSet(SqlSet set, SqlBuilder superQuery, Type resultType) 
         : this(set, superQuery) {

         this.resultType = resultType;
      }

      [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate", Justification = "Calling the member twice in succession creates different results.")]
      public SqlBuilder GetDefiningQuery() {
         return this.definingQuery.Clone();
      }

      protected SqlBuilder CreateSuperQuery() {
         return CreateSuperQuery(null, null);
      }

      protected SqlBuilder CreateSuperQuery(string selectFormat, params object[] args) { 
         
         return new SqlBuilder()
            .SELECT(selectFormat ?? "*", args)
            .FROM(this.definingQuery, "__set" + this.setIndex.ToString(CultureInfo.InvariantCulture));
      }

      protected DbCommand CreateCommand() {
         return CreateCommand(this.definingQuery);
      }

      protected DbCommand CreateCommand(SqlBuilder sqlBuilder) {

         if (sqlBuilder == null) throw new ArgumentNullException("sqlBuilder");

         return CreateCommand(sqlBuilder.ToString(), sqlBuilder.ParameterValues.ToArray());
      }

      protected DbCommand CreateCommand(string commandText) {
         return CreateCommand(commandText, null);
      }

      protected virtual DbCommand CreateCommand(string commandText, params object[] parameters) {
         return this.connection.CreateCommand(commandText, parameters);
      }

      public IEnumerator<object> GetEnumerator() {
         return AsEnumerable().GetEnumerator();
      }

      [EditorBrowsable(EditorBrowsableState.Never)]
      public override bool Equals(object obj) {
         return base.Equals(obj);
      }

      [EditorBrowsable(EditorBrowsableState.Never)]
      public override int GetHashCode() {
         return base.GetHashCode();
      }

      [EditorBrowsable(EditorBrowsableState.Never)]
      [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate", Justification = "Must match base signature.")]
      public new Type GetType() {
         return base.GetType();
      }

      public override string ToString() {
         return this.definingQuery.ToString();
      }

      #region ISqlSet<SqlSet,object> Members

      public bool All(string predicate) {
         return All(predicate, null);
      }

      public bool All(string predicate, params object[] parameters) {

         using (this.connection.EnsureOpen())
            return LongCount() == Where(predicate, parameters).LongCount();
      }

      /// <summary>
      /// Checks if the set contains rows.
      /// </summary>
      /// <returns>true id the set contains rows; otherwise, false.</returns>
      public bool Any() {
         return this.connection.Exists(CreateCommand(SqlBuilderDbExtensions.ExistsQuery(this.definingQuery)), this.Log);
      }

      public bool Any(string predicate) {
         return Any(predicate, null);
      }

      /// <summary>
      /// Checks if <paramref name="predicate"/> matches any of the rows in the set.
      /// </summary>
      /// <param name="predicate">The SQL predicate.</param>
      /// <param name="parameters">The parameters to use in the predicate.</param>
      /// <returns>true if at least one row matches the <paramref name="predicate"/>; otherwise, false.</returns>
      public bool Any(string predicate, params object[] parameters) {

         var superQuery = CreateSuperQuery()
            .WHERE()
            ._If(!String.IsNullOrEmpty(predicate), predicate, parameters);

         return this.connection.Exists(CreateCommand(SqlBuilderDbExtensions.ExistsQuery(superQuery)), this.Log);
      }

      /// <summary>
      /// Gets all objects in the set. The query is deferred-executed.
      /// </summary>
      /// <returns>All objects in the set.</returns>
      public IEnumerable<object> AsEnumerable() {

         if (this.resultType == null)
            throw new InvalidOperationException("Cannot map set, a result type was not specified when this set was created. Call the 'Cast' method first.");

         return CreateCommand().Map(resultType, this.Log);
      }

      public SqlSet<TResult> Cast<TResult>() {
         return new SqlSet<TResult>(this, GetDefiningQuery());
      }

      public SqlSet Cast(Type resultType) {
         return new SqlSet(this, GetDefiningQuery(), resultType);
      }

      public int Count() {
         return this.connection.Count(CreateCommand(SqlBuilderDbExtensions.CountQuery(this.definingQuery)), this.Log);
      }

      public int Count(string predicate) {
         return Count(predicate, null);
      }

      /// <summary>
      /// Gets the number of rows in the set that matches the <paramref name="predicate"/>.
      /// </summary>
      /// <param name="predicate">The SQL predicate.</param>
      /// <param name="parameters">The parameters to use in the predicate.</param>
      /// <returns>The number of rows that match the <paramref name="predicate"/>.</returns>
      public int Count(string predicate, params object[] parameters) {

         var superQuery = CreateSuperQuery()
            .WHERE()
            ._If(!String.IsNullOrEmpty(predicate), predicate, parameters);

         return this.connection.Count(CreateCommand(SqlBuilderDbExtensions.CountQuery(superQuery)), this.Log);
      }

      public object First() {
         return Take(1).AsEnumerable().First();
      }

      public object First(string predicate) {
         return First(predicate, null);
      }

      public object First(string predicate, params object[] parameters) {
         return Where(predicate, parameters).First();
      }

      public object FirstOrDefault() {
         return Take(1).AsEnumerable().FirstOrDefault();
      }

      public object FirstOrDefault(string predicate) {
         return FirstOrDefault(predicate, null);
      }

      public object FirstOrDefault(string predicate, params object[] parameters) {
         return Where(predicate, parameters).FirstOrDefault();
      }

      [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "long", Justification = "Consistent with LINQ.")]
      public long LongCount() {
         return this.connection.LongCount(CreateCommand(SqlBuilderDbExtensions.CountQuery(this.definingQuery)), this.Log);
      }

      public long LongCount(string predicate) {
         return LongCount(predicate, null);
      }

      /// <summary>
      /// Gets the number of rows in the set that matches the <paramref name="predicate"/>.
      /// </summary>
      /// <param name="predicate">The SQL predicate.</param>
      /// <param name="parameters">The parameters to use in the predicate.</param>
      /// <returns>The number of rows that match the <paramref name="predicate"/>.</returns>
      [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "long", Justification = "Consistent with LINQ.")]
      public long LongCount(string predicate, params object[] parameters) {

         var superQuery = CreateSuperQuery()
            .WHERE()
            ._If(!String.IsNullOrEmpty(predicate), predicate, parameters);

         return this.connection.LongCount(CreateCommand(SqlBuilderDbExtensions.CountQuery(superQuery)), this.Log);
      }

      public SqlSet OrderBy(string format) {
         return OrderBy(format, null);
      }

      public SqlSet OrderBy(string format, params object[] args) {

         var superQuery = CreateSuperQuery()
            .ORDER_BY(format, args);

         return new SqlSet(this, superQuery);
      }

      public SqlSet<TResult> Select<TResult>(string format) {
         return Select<TResult>(format, null);
      }

      public SqlSet<TResult> Select<TResult>(string format, params object[] args) {

         var superQuery = CreateSuperQuery(format, args);

         return new SqlSet<TResult>(this, superQuery);
      }

      public SqlSet<TResult> Select<TResult>(Func<IDataRecord, TResult> mapper, string format) {
         return Select<TResult>(mapper, format, null);
      }

      public SqlSet<TResult> Select<TResult>(Func<IDataRecord, TResult> mapper, string format, params object[] args) {

         var superQuery = CreateSuperQuery(format, args);

         return new SqlSet<TResult>(this, superQuery, mapper);
      }

      public SqlSet Select(Type resultType, string format) {
         return Select(resultType, format, null);
      }

      public SqlSet Select(Type resultType, string format, params object[] args) {

         var superQuery = CreateSuperQuery(format, args);

         return new SqlSet(this, superQuery, resultType);
      }

      public object Single() {
         
         // Query could return millions of records
         // just select 2 then call Single
         return Take(2).AsEnumerable().Single();
      }

      public object Single(string predicate) {
         return Single(predicate, null);
      }

      public object Single(string predicate, params object[] parameters) {
         return Where(predicate, parameters).Single();
      }

      public object SingleOrDefault() {

         // Query could return millions of records
         // just select 2 then call SingleOrDefault
         return Take(2).AsEnumerable().SingleOrDefault();
      }

      public object SingleOrDefault(string predicate) {
         return SingleOrDefault(predicate, null);
      }

      public object SingleOrDefault(string predicate, params object[] parameters) {
         return Where(predicate, parameters).SingleOrDefault();
      }

      public SqlSet Skip(int count) {

         var superQuery = CreateSuperQuery()
            .OFFSET(count);

         return new SqlSet(this, superQuery);
      }

      public SqlSet Take(int count) {

         var superQuery = CreateSuperQuery()
            .LIMIT(count);

         return new SqlSet(this, superQuery);
      }

      public object[] ToArray() {
         return AsEnumerable().ToArray();
      }

      public List<object> ToList() {
         return AsEnumerable().ToList();
      }

      public SqlSet Where(string predicate) {
         return Where(predicate, null);
      }

      public SqlSet Where(string predicate, params object[] parameters) {

         var superQuery = CreateSuperQuery()
            .WHERE(predicate, parameters);

         return new SqlSet(this, superQuery);
      }

      public SqlSet Union(SqlSet otherSet) {

         if (otherSet == null) throw new ArgumentNullException("otherSet");

         var superQuery = CreateSuperQuery()
            .UNION()
            .Append(otherSet.CreateSuperQuery());

         return new SqlSet(this, superQuery);
      }

      #endregion
   }

   public class SqlSet<TResult> : SqlSet, ISqlSet<SqlSet<TResult>, TResult> {

      readonly Func<IDataRecord, TResult> mapper;

      public SqlSet(DbConnection connection, SqlBuilder definingQuery) 
         : base(connection, definingQuery, typeof(TResult)) { }

      public SqlSet(DbConnection connection, SqlBuilder definingQuery, Func<IDataRecord, TResult> mapper)
         : this(connection, definingQuery) {

         this.mapper = mapper;
      }

      protected SqlSet(DbConnection connection, SqlBuilder definingQuery, bool adoptQuery)
         : base(connection, definingQuery, typeof(TResult), adoptQuery) { }

      protected SqlSet(DbConnection connection, SqlBuilder definingQuery, Func<IDataRecord, TResult> mapper, bool adoptQuery) 
         : base(connection, definingQuery, adoptQuery) {

         this.mapper = mapper;
      }

      protected SqlSet(SqlSet<TResult> set, SqlBuilder superQuery) 
         : this((SqlSet)set, superQuery) {

         if (set == null) throw new ArgumentNullException("set");

         this.mapper = set.mapper;
      }

      protected internal SqlSet(SqlSet set, SqlBuilder superQuery)
         : base(set, superQuery) { }

      protected internal SqlSet(SqlSet set, SqlBuilder superQuery, Func<IDataRecord, TResult> mapper)
         : this(set, superQuery) {

         this.mapper = mapper;
      }

      public new IEnumerator<TResult> GetEnumerator() {
         return AsEnumerable().GetEnumerator();
      }

      #region ISqlSet<SqlSet<TResult>,TResult> Members

      /// <summary>
      /// Gets all <typeparamref name="TResult"/> objects in the set. The query is deferred-executed.
      /// </summary>
      /// <returns>All <typeparamref name="TResult"/> objects in the set.</returns>
      public new IEnumerable<TResult> AsEnumerable() {

         DbCommand command = CreateCommand();

         if (this.mapper != null)
            return command.Map(this.mapper, this.Log);

         return command.Map<TResult>(this.Log);
      }

      public new TResult First() {
         return Take(1).AsEnumerable().First();
      }

      public new TResult First(string predicate) {
         return First(predicate, null);
      }

      public new TResult First(string predicate, params object[] parameters) {
         return Where(predicate, parameters).First();
      }

      public new TResult FirstOrDefault() {
         return Take(1).AsEnumerable().FirstOrDefault();
      }

      public new TResult FirstOrDefault(string predicate) {
         return FirstOrDefault(predicate, null);
      }

      public new TResult FirstOrDefault(string predicate, params object[] parameters) {
         return Where(predicate, parameters).FirstOrDefault();
      }

      public new SqlSet<TResult> OrderBy(string format) {
         return OrderBy(format, null);
      }

      public new SqlSet<TResult> OrderBy(string format, params object[] args) {

         var superQuery = CreateSuperQuery()
            .ORDER_BY(format, args);

         return new SqlSet<TResult>(this, superQuery);
      }

      public new TResult Single() {

         // Query could return millions of records
         // just select 2 then call Single
         return Take(2).AsEnumerable().Single();
      }

      public new TResult Single(string predicate) {
         return Single(predicate, null);
      }

      public new TResult Single(string predicate, params object[] parameters) {
         return Where(predicate, parameters).Single();
      }

      public new TResult SingleOrDefault() {

         // Query could return millions of records
         // just select 2 then call SingleOrDefault
         return Take(2).AsEnumerable().SingleOrDefault();
      }

      public new TResult SingleOrDefault(string predicate) {
         return SingleOrDefault(predicate, null);
      }

      public new TResult SingleOrDefault(string predicate, params object[] parameters) {
         return Where(predicate, parameters).SingleOrDefault();
      }

      public new SqlSet<TResult> Skip(int count) {

         var superQuery = CreateSuperQuery()
            .OFFSET(count);

         return new SqlSet<TResult>(this, superQuery);
      }

      public new SqlSet<TResult> Take(int count) {

         var superQuery = CreateSuperQuery()
            .LIMIT(count);

         return new SqlSet<TResult>(this, superQuery);
      }

      public new TResult[] ToArray() {
         return AsEnumerable().ToArray();
      }

      public new List<TResult> ToList() {
         return AsEnumerable().ToList();
      }

      public new SqlSet<TResult> Where(string predicate) {
         return Where(predicate, null);
      }

      public new SqlSet<TResult> Where(string predicate, params object[] parameters) {

         var superQuery = CreateSuperQuery()
            .WHERE(predicate, parameters);

         return new SqlSet<TResult>(this, superQuery);
      }

      public SqlSet<TResult> Union(SqlSet<TResult> otherSet) {

         if (otherSet == null) throw new ArgumentNullException("otherSet");

         var superQuery = CreateSuperQuery()
            .UNION()
            .Append(otherSet.CreateSuperQuery());

         return new SqlSet<TResult>(this, superQuery);
      }

      #endregion
   }
   
   interface ISqlSet<TSqlSet, TSource> where TSqlSet : SqlSet {

      // Standard SqlSet API
      // aka LINQ to SqlBuilder

      bool All(string predicate);
      bool All(string predicate, params object[] parameters);
      bool Any();
      bool Any(string predicate);
      bool Any(string predicate, params object[] parameters);
      IEnumerable<TSource> AsEnumerable();
      SqlSet<TResult> Cast<TResult>();
      SqlSet Cast(Type resultType);
      int Count();
      int Count(string predicate);
      int Count(string predicate, params object[] parameters);
      TSource First();
      TSource First(string predicate);
      TSource First(string predicate, params object[] parameters);
      TSource FirstOrDefault();
      TSource FirstOrDefault(string predicate);
      TSource FirstOrDefault(string predicate, params object[] parameters);
      long LongCount();
      long LongCount(string predicate);
      long LongCount(string predicate, params object[] parameters);
      TSqlSet OrderBy(string format);
      TSqlSet OrderBy(string format, params object[] args);
      SqlSet<TResult> Select<TResult>(string format);
      SqlSet<TResult> Select<TResult>(string format, params object[] args);
      SqlSet<TResult> Select<TResult>(Func<IDataRecord, TResult> mapper, string format);
      SqlSet<TResult> Select<TResult>(Func<IDataRecord, TResult> mapper, string format, params object[] args);
      SqlSet Select(Type resultType, string format);
      SqlSet Select(Type resultType, string format, params object[] args);
      TSource Single();
      TSource Single(string predicate);
      TSource Single(string predicate, params object[] parameters);
      TSource SingleOrDefault();
      TSource SingleOrDefault(string predicate);
      TSource SingleOrDefault(string predicate, params object[] parameters);
      TSqlSet Skip(int count);
      TSqlSet Take(int count);
      TSource[] ToArray();
      List<TSource> ToList();
      TSqlSet Where(string predicate);
      TSqlSet Where(string predicate, params object[] parameters);
      TSqlSet Union(TSqlSet otherSet);
   }
}