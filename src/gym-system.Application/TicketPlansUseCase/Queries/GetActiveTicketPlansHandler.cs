using System;
using System.Collections.Generic;
using System.Text;

namespace gym_system.Application.TicketPlansUseCase.Queries
{
    public sealed class GetActiveTicketPlansHandler
    {
        private readonly ITicketPlanCatalogQuerySerivce _queryService;

        public GetActiveTicketPlansHandler(ITicketPlanCatalogQuerySerivce querySerivce)
        {
            _queryService= querySerivce;
        }

        public Task<IReadOnlyList<TicketPlanResult>> Handle(CancellationToken ct = default)
        => _queryService.GetActiveTicketPlansAsync(ct);
    }
}
