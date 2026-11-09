using System.Linq.Expressions;
using static LeftJoin.E2E.Tests.SampleData;

namespace LeftJoin.E2E.Tests;

public class TranslationTests
{
    [Fact]
    public void LeftJoin_OverQueryable_BuildsQueryableLeftJoinCall()
    {
        var query =
            from s in Students.AsQueryable()
            leftjoin d in Departments on s.DepartmentId equals d.Id
            select new { s.Name, Department = d == null ? null : d.Name };

        var call = Assert.IsAssignableFrom<MethodCallExpression>(query.Expression);
        Assert.Equal(typeof(Queryable), call.Method.DeclaringType);
        Assert.Equal(nameof(Queryable.LeftJoin), call.Method.Name);
        Assert.Equal(5, query.Count());
    }

    [Fact]
    public void RightJoin_OverQueryable_BuildsQueryableRightJoinCall()
    {
        var query =
            from d in Departments.AsQueryable()
            rightjoin s in Students on d.Id equals s.DepartmentId
            select new { s.Name, Department = d == null ? null : d.Name };

        var call = Assert.IsAssignableFrom<MethodCallExpression>(query.Expression);
        Assert.Equal(typeof(Queryable), call.Method.DeclaringType);
        Assert.Equal(nameof(Queryable.RightJoin), call.Method.Name);
        Assert.Equal(5, query.Count());
    }

    [Fact]
    public void LeftJoin_FollowedBySelect_CallsOnlyLeftJoin()
    {
        var log = new List<string>();

        var result =
            from s in new RecordingSource<Student>(Students, log)
            leftjoin d in Departments on s.DepartmentId equals d.Id
            select s.Name;

        Assert.Equal(["LeftJoin"], log);
        Assert.Equal(5, result.Items.Count());
    }

    [Fact]
    public void RightJoin_FollowedBySelect_CallsOnlyRightJoin()
    {
        var log = new List<string>();

        var result =
            from d in new RecordingSource<Department>(Departments, log)
            rightjoin s in Students on d.Id equals s.DepartmentId
            select s.Name;

        Assert.Equal(["RightJoin"], log);
        Assert.Equal(5, result.Items.Count());
    }

    [Fact]
    public void LeftJoin_FollowedByWhere_CallsLeftJoinWhereSelect()
    {
        var log = new List<string>();

        var result =
            from s in new RecordingSource<Student>(Students, log)
            leftjoin d in Departments on s.DepartmentId equals d.Id
            where d == null
            select s.Name;

        Assert.Equal(["LeftJoin", "Where", "Select"], log);
        Assert.Equal(["Linus", "Barbara"], result.Items);
    }
}

/// <summary>
/// Query-pattern source that records which operators the compiler translated a query into.
/// </summary>
public sealed class RecordingSource<T>(IEnumerable<T> items, List<string> log)
{
    public IEnumerable<T> Items => items;

    public RecordingSource<TResult> Join<TInner, TKey, TResult>(
        IEnumerable<TInner> inner, Func<T, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector, Func<T, TInner, TResult> resultSelector)
        => Record(nameof(Join), items.Join(inner, outerKeySelector, innerKeySelector, resultSelector));

    public RecordingSource<TResult> LeftJoin<TInner, TKey, TResult>(
        IEnumerable<TInner> inner, Func<T, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector, Func<T, TInner?, TResult> resultSelector)
        => Record(nameof(LeftJoin), items.LeftJoin(inner, outerKeySelector, innerKeySelector, resultSelector));

    public RecordingSource<TResult> RightJoin<TInner, TKey, TResult>(
        IEnumerable<TInner> inner, Func<T, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector, Func<T?, TInner, TResult> resultSelector)
        => Record(nameof(RightJoin), items.RightJoin(inner, outerKeySelector, innerKeySelector, resultSelector));

    public RecordingSource<TResult> GroupJoin<TInner, TKey, TResult>(
        IEnumerable<TInner> inner, Func<T, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector, Func<T, IEnumerable<TInner>, TResult> resultSelector)
        => Record(nameof(GroupJoin), items.GroupJoin(inner, outerKeySelector, innerKeySelector, resultSelector));

    public RecordingSource<TResult> SelectMany<TCollection, TResult>(
        Func<T, IEnumerable<TCollection>> collectionSelector, Func<T, TCollection, TResult> resultSelector)
        => Record(nameof(SelectMany), items.SelectMany(collectionSelector, resultSelector));

    public RecordingSource<TResult> Select<TResult>(Func<T, TResult> selector)
        => Record(nameof(Select), items.Select(selector));

    public RecordingSource<T> Where(Func<T, bool> predicate)
        => Record(nameof(Where), items.Where(predicate));

    private RecordingSource<TResult> Record<TResult>(string method, IEnumerable<TResult> result)
    {
        log.Add(method);
        return new RecordingSource<TResult>(result, log);
    }
}
