using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace MyMemoryStore;

/// <summary>
/// 永続化インターフェース
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IEntityPersister<T>
    where T: BaseEntity
{
    void Add(T entity);
    void Update(T entity);
    void Remove(string id);
}
public interface IConverter<T, TView>
    where T: BaseEntity
    where TView: BaseView
{
    TView ToView(T entity);
    T FromView(TView view);
}

public sealed class SimpleEntityStore<T, TView>: IDisposable
    where T: BaseEntity
    where TView: BaseView
{
    private readonly ConcurrentDictionary<string, T> _list = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly IEntityPersister<T> _persister;
    private readonly IConverter<T, TView> _converter;

    /// <summary>
    /// Observable for any record added/updated/removed.
    /// </summary>
    /// <returns></returns>
    private readonly Subject<TView> _added = new();
    private readonly Subject<TView> _updated = new();
    private readonly Subject<TView> _removed = new();

    public IObservable<TView> ObserveAdded() => _added.AsObservable();
    public IObservable<TView> ObserveUpdated() => _updated.AsObservable();
    public IObservable<TView> ObserveRemoved() => _removed.AsObservable();

    public SimpleEntityStore(
        IEntityPersister<T> persister,
        IConverter<T, TView> converter)
    {
        _persister = persister;
        _converter = converter;
    }

    // ---- 参照系 ----
    public TView? Find(string id)
    {
        var gate = GetGate(id);
        gate.Wait();
        try
        {
            if (!_list.ContainsKey(id))
            {
                return default;
            }
            return _converter.ToView(_list[id]);
        }
        finally
        {
            gate.Release();
        }
    }
    public IReadOnlyList<TView> All()
    {
        return _list.Values.Select(_converter.ToView).ToList();
    }

    // ---- 更新系 ----
    public void Add(TView data)
    {
        var gate = GetGate(data.Id);
        gate.Wait();
        try
        {
            if (_list.ContainsKey(data.Id))
            {
                throw new InvalidOperationException($"Id '{data.Id}' already exists.");
            }
            var entity = _converter.FromView(data);
            _list[entity.Id] = entity;
            _persister.Add(entity);
            _added.OnNext(data);
        }
        finally
        {
            gate.Release();
        }
    }
    public void Update(TView data)
    {
        var gate = GetGate(data.Id);
        gate.Wait();
        try
        {
            if (!_list.ContainsKey(data.Id))
            {
                throw new InvalidOperationException($"Id '{data.Id}' not found.");
            }
            var entity = _converter.FromView(data);
            _list[entity.Id].Update(entity);
            _persister.Update(entity);
            _updated.OnNext(data);
        }
        finally
        {
            gate.Release();
        }
    }
    public void Remove(string id)
    {
        var gate = GetGate(id);
        gate.Wait();
        try
        {
            if (!_list.ContainsKey(id))
            {
                // 削除予定の物が無い場合は何もしない
                return;
            }
            T tmp;
            _list.Remove(id, out tmp);
            _persister.Remove(id);
            _removed.OnNext(_converter.ToView(tmp));
        }
        finally
        {
            gate.Release();
        }
    }

    // ---- 内部: 鍵毎のロックと Subject 取得 ----
    private SemaphoreSlim GetGate(string id) => _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));

    public void Dispose()
    {
        _added.Dispose();
        _updated.Dispose();
        _removed.Dispose();
        foreach (var sem in _locks.Values)
        {
            sem.Dispose();
        }
    }
}
