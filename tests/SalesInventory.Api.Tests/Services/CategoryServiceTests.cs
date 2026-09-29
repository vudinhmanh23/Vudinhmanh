using Moq;
using SalesInventory.Domain.Entities;
using SalesInventory.Application.Interfaces;
using SalesInventory.Application.Services;

namespace SalesInventory.Api.Tests.Services;

public class CategoryServiceTests
{
    [Fact]
    public async Task CreateCategoryAsync_EmptyName_ThrowsArgumentException()
    {
        // Arrange
        var repositoryMock = new Mock<IRepository<Category>>();
        var service = new CategoryService(repositoryMock.Object);
        var category = new Category { Name = "" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateCategoryAsync(category));

        // The repository must never be touched when validation fails
        repositoryMock.Verify(r => r.AddAsync(It.IsAny<Category>()), Times.Never);
        repositoryMock.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task CreateCategoryAsync_ValidName_CallsAddAsyncOnce()
    {
        // Arrange
        var repositoryMock = new Mock<IRepository<Category>>();
        var service = new CategoryService(repositoryMock.Object);
        var category = new Category { Name = "Đồ điện tử" };

        // Act
        var result = await service.CreateCategoryAsync(category);

        // Assert
        repositoryMock.Verify(r => r.AddAsync(category), Times.Once);
        repositoryMock.Verify(r => r.SaveChangesAsync(), Times.Once);
        Assert.Equal(category, result);
    }
}
