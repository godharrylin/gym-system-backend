using gym_system.Api.Contracts.Courses;
using gym_system.Api.Controllers;
using gym_system.Application.CoursesUseCase.Queries;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace gym_system.Api.Tests
{
    public sealed class ClassControllerTests
    {
        [Fact]
        public async Task GetClassesAsync_ShouldReturnMappedResponse()
        {
            var fake = new FakeClassCatalogQueryService
            {
                Result =
                [
                    new CourseResult
                    {
                        class_sn = 1,
                        class_name = "瑜珈入門",
                        usr_name = "Amy",
                        class_label_color = "#FF5733",
                        class_duration = 60,
                        class_is_free = true,
                        class_is_active = true
                    }
                ]
            };

            var handler = new GetCoursesListHandler(fake);
            var sut = new CourseController(handler, null!, null!);

            var action = await sut.GetCoursesAsync(includeInactive: false, CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(action.Result);
            var response = Assert.IsType<GetCourseResponse>(ok.Value);
            var item = Assert.Single(response.CoursesInfoList!);

            Assert.Equal("1", item.Id);
            Assert.Equal("瑜珈入門", item.Name);
            Assert.Equal("Amy", item.Instructor);
            Assert.Equal("#FF5733", item.Color);
            Assert.Equal("60", item.Duration);
            Assert.True(item.IsFree);
            Assert.True(item.IsActive);
        }

        [Fact]
        public async Task GetClassesAsync_ShouldForwardIncludeInactiveToQueryService()
        {
            var fake = new FakeClassCatalogQueryService();
            var handler = new GetCoursesListHandler(fake);
            var sut = new CourseController(handler, null!, null!);

            await sut.GetCoursesAsync(includeInactive: true, CancellationToken.None);

            Assert.True(fake.LastIncludeInactive);
        }

        [Fact]
        public async Task GetClassesAsync_ShouldReturnEmptyList_WhenNoData()
        {
            var fake = new FakeClassCatalogQueryService
            {
                Result = []
            };

            var handler = new GetCoursesListHandler(fake);
            var sut = new CourseController(handler, null!, null!);

            var action = await sut.GetCoursesAsync(includeInactive: null, CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(action.Result);
            var response = Assert.IsType<GetCourseResponse>(ok.Value);

            Assert.NotNull(response.CoursesInfoList);
            Assert.Empty(response.CoursesInfoList!);
            Assert.Null(fake.LastIncludeInactive);
        }

        private sealed class FakeClassCatalogQueryService : ICourseCatalogQueryService
        {
            public bool? LastIncludeInactive { get; private set; }
            public IReadOnlyList<CourseResult> Result { get; set; } = [];

            public Task<IReadOnlyList<CourseResult>> GetClassesAsync(bool? includeInactive, CancellationToken ct)
            {
                LastIncludeInactive = includeInactive;
                return Task.FromResult(Result);
            }
        }
    }
}
