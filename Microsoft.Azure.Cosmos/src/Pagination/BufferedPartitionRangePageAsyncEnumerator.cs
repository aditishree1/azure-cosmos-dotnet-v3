﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Pagination
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Query.Core.Monads;
    using Microsoft.Azure.Cosmos.Tracing;

    internal sealed class BufferedPartitionRangePageAsyncEnumerator<TPage, TState> : BufferedPartitionRangePageAsyncEnumeratorBase<TPage, TState>
        where TPage : Page<TState>
        where TState : State
    {
        private readonly PartitionRangePageAsyncEnumerator<TPage, TState> enumerator;
        private TryCatch<TPage>? bufferedPage;

        public override Exception BufferedException
        {
            get
            {
                if (this.bufferedPage.HasValue && this.bufferedPage.Value.Failed)
                {
                    return this.bufferedPage.Value.Exception;
                }

                return null;
            }
        }

        public override int BufferedItemCount => this.bufferedPage.HasValue && this.bufferedPage.Value.Succeeded ?
            this.bufferedPage.Value.Result.ItemCount :
            0;

        public BufferedPartitionRangePageAsyncEnumerator(PartitionRangePageAsyncEnumerator<TPage, TState> enumerator)
            : base(enumerator.FeedRangeState)
        {
            this.enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        }

        public override ValueTask DisposeAsync()
        {
            return this.enumerator.DisposeAsync();
        }

        protected override async Task<TryCatch<TPage>> GetNextPageAsync(ITrace trace, CancellationToken cancellationToken)
        {
            if (trace == null)
            {
                throw new ArgumentNullException(nameof(trace));
            }

            await this.PrefetchAsync(trace, cancellationToken);

            // Serve from the buffered page first.
            TryCatch<TPage> returnValue = this.bufferedPage.Value;
            this.bufferedPage = null;
            return returnValue;
        }

        public override async ValueTask PrefetchAsync(ITrace trace, CancellationToken cancellationToken)
        {
            if (trace == null)
            {
                throw new ArgumentNullException(nameof(trace));
            }

            if (this.bufferedPage.HasValue)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            using (ITrace prefetchTrace = trace.StartChild("Prefetch", TraceComponent.Pagination, TraceLevel.Info))
            {
                await this.enumerator.MoveNextAsync(prefetchTrace, cancellationToken);
                this.bufferedPage = this.enumerator.Current;
            }
        }
    }
}
