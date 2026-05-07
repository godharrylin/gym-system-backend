using gym_system.Api.Contracts.Instructors;
using gym_system.Api.Controllers;
using gym_system.Application.InstructorUseCase.Queries;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace gym_system.Api.Tests
{
    public sealed class InstructorControllerTests
    {
        [Fact]
        public async Task GetInstructorsAsync_ShouldReturnMappedResponse()
        {
            var fake = new FakeInstructorQueryService
            {
                Result =
                [
                    new InstrucotrResult
                    {
                        usr_id = "U0000000001",
                        usr_name = "Amy",
                        usr_phone = "0912345678",
                        user_role_is_active = true
                    }
                ]
            };

            var getHandler = new GetInstructorsListHandler(fake);
            var sut = new InstructorController(getHandler, null!, null!);

            var action = await sut.GetInstructorsAsync(CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(action);
            var response = Assert.IsType<GetInstructorsResponse>(ok.Value);
            var instructor = Assert.Single(response.InstructorList!);

            Assert.Equal("U0000000001", instructor.Id);
            Assert.Equal("Amy", instructor.Name);
            Assert.Equal("0912345678", instructor.Phone);
            Assert.True(instructor.isActived);
        }

        [Fact]
        public async Task GetInstructorsAsync_ShouldReturnEmptyList_WhenNoData()
        {
            var fake = new FakeInstructorQueryService
            {
                Result = []
            };

            var getHandler = new GetInstructorsListHandler(fake);
            var sut = new InstructorController(getHandler, null!, null!);

            var action = await sut.GetInstructorsAsync(CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(action);
            var response = Assert.IsType<GetInstructorsResponse>(ok.Value);

            Assert.NotNull(response.InstructorList);
            Assert.Empty(response.InstructorList!);
        }

        private sealed class FakeInstructorQueryService : IInstructorQueryService
        {
            public IReadOnlyList<InstrucotrResult> Result { get; set; } = [];

            public Task<IReadOnlyList<InstrucotrResult>> GetInstructorsAsync(CancellationToken ct)
            {
                return Task.FromResult(Result);
            }
        }
    }
}
