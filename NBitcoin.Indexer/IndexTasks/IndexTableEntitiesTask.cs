﻿using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NBitcoin.Indexer.IndexTasks
{
    public class IndexTableEntitiesTask : IndexTableEntitiesTaskBase<ITableEntity>
    {
        CloudTable _Table;
        public IndexTableEntitiesTask(IndexerConfiguration conf, CloudTable table)
            : base(conf)
        {
            _Table = table;
        }

        protected override CloudTable GetCloudTable()
        {
            return _Table;
        }

        protected override ITableEntity ToTableEntity(ITableEntity item)
        {
            return item;
        }

        protected override void ProcessBlock(BlockInfo block, BulkImport<ITableEntity> bulk)
        {
            throw new NotSupportedException();
        }


        public void Index(IEnumerable<ITableEntity> entities)
        {
            try
            {
                IndexAsync(entities).Wait();
            }
            catch (AggregateException aex)
            {
                ExceptionDispatchInfo.Capture(aex.InnerException).Throw();
                throw;
            }
        }
        public Task IndexAsync(IEnumerable<ITableEntity> entities)
        {
            var tasks = entities
                .GroupBy(e => e.PartitionKey)
                .SelectMany(group => group
                                    .Partition(100)
                                    .Select(batch => IndexCore(group.Key, batch)))
                .ToArray();
            return Task.WhenAll(tasks);
        }
    }
    public abstract class IndexTableEntitiesTaskBase<TIndexed> : IndexTask<TIndexed>
    {
        public IndexTableEntitiesTaskBase(IndexerConfiguration configuration)
            : base(configuration)
        {

        }

        int _IndexedEntities = 0;
        public int IndexedEntities
        {
            get
            {
                return _IndexedEntities;
            }
        }
        protected override int PartitionSize
        {
            get
            {
                return 100;
            }
        }





        protected override Task EnsureSetup()
        {
            return GetCloudTable().CreateIfNotExistsAsync();
        }

        protected abstract CloudTable GetCloudTable();
        protected abstract ITableEntity ToTableEntity(TIndexed item);

        protected override async Task IndexCore(string partitionName, IEnumerable<TIndexed> items)
        {
            var batch = new TableBatchOperation();
            foreach(var item in items)
            {
                batch.Add(TableOperation.InsertOrReplace(ToTableEntity(item)));
            }

            var table = GetCloudTable();

            int exceptionCount = 0;
            var options = new TableRequestOptions()
            {
                PayloadFormat = TablePayloadFormat.Json,
                MaximumExecutionTime = _Timeout,
                ServerTimeout = _Timeout,
            };





            Queue<TableBatchOperation> batches = new Queue<TableBatchOperation>();
            batches.Enqueue(batch);
            bool needWait = false;


            while (batches.Count > 0)
            {
                if (needWait)
                {
                    needWait = false;
                    await Task.Delay(exceptionCount * 1000).ConfigureAwait(false);
                }
                batch = batches.Dequeue();
                try
                {
                    if (batch.Count > 1)
                        await table.ExecuteBatchAsync(batch, options, null).ConfigureAwait(false);
                    else
                    {
                        if (batch.Count == 1)
                            await table.ExecuteAsync(batch[0], options, null).ConfigureAwait(false);
                    }

                    Interlocked.Add(ref _IndexedEntities, batch.Count);
                    if (exceptionCount != 0)
                    {
                        exceptionCount = 0;
                        IndexerTrace.RetryWorked();
                    }
                }
                catch (Exception ex)
                {
                    if (IsError413(ex))
                    {
                        var split = batch.Count / 2;
                        var batch1 = batch.Take(split).ToList();
                        var batch2 = batch.Skip(split).Take(batch.Count - split).ToList();
                        batches.Enqueue(ToBatch(batch1));
                        batches.Enqueue(ToBatch(batch2));
                    }
                    else if (IsError(ex, "EntityTooLarge"))
                    {
                        var op = GetFaultyOperation(ex, batch);
                        batch.Remove(op);
                        batches.Enqueue(batch);
                    }
                    else
                    {
                        IndexerTrace.ErrorWhileImportingEntitiesToAzure(batch.Select(b => GetEntity(b)).ToArray(), ex);
                        exceptionCount++;
                        batches.Enqueue(batch);
                        if (exceptionCount > 5)
                            throw;
                        needWait = true;
                    }
                }
            }
        }


        private ITableEntity GetEntity(TableOperation op)
        {
            return (ITableEntity)typeof(TableOperation).GetProperty("Entity", BindingFlags.Instance |
                            BindingFlags.NonPublic |
                            BindingFlags.Public)
                            .GetValue(op);
        }


        private bool IsError413(Exception ex)
        {
            var storage = ex as StorageException;
            if (storage == null)
                return false;
            return storage.RequestInformation != null && storage.RequestInformation.HttpStatusCode == 413;
        }

        private TableOperation GetFaultyOperation(Exception ex, TableBatchOperation batch)
        {
            var storage = ex as StorageException;
            if (storage == null)
                return null;
            if (storage.RequestInformation != null
               && storage.RequestInformation.ExtendedErrorInformation != null)
            {
                var match = Regex.Match(storage.RequestInformation.ExtendedErrorInformation.ErrorMessage, "[0-9]*");
                if (match.Success)
                    return batch[int.Parse(match.Value)];
            }
            return null;
        }
        private TableBatchOperation ToBatch(List<TableOperation> ops)
        {
            var op = new TableBatchOperation();
            foreach (var operation in ops)
                op.Add(operation);
            return op;
        }

        private bool IsError(Exception ex, string code)
        {
            var storage = ex as StorageException;
            if (storage == null)
                return false;
            return storage.RequestInformation != null
                && storage.RequestInformation.ExtendedErrorInformation != null
                && storage.RequestInformation.ExtendedErrorInformation.ErrorCode == code;
        }


    }
}