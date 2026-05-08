using System;
using System.Collections.Generic;
using System.Text;

namespace gym_system.Application.TicketPlansUseCase.Queries
{
    public sealed class GetActiveTicketPlansHandler
    {
        private readonly ITicketPlanCatalogQueryService _queryService;

        public GetActiveTicketPlansHandler(ITicketPlanCatalogQueryService queryService)
        {
            _queryService= queryService;
        }

        public Task<IReadOnlyList<TicketPlanResult>> Handle(CancellationToken ct = default)
        => _queryService.GetActiveTicketPlansAsync(ct);
    }
}
