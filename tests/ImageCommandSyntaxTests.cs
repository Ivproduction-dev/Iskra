using IskraStudio;
using Xunit;

namespace IskraStudio.Tests;

public sealed class ImageCommandSyntaxTests
{
    [Fact]
    public void LocalImageReferenceKeepsCurrentObjectAndFilename()
    {
        var currentObjectId = Guid.NewGuid();
        var objects = new[] { new IskraObject { Id = Guid.NewGuid(), Name = "Игрок" } };

        var reference = ImageCommandSyntax.ResolveReference("улыбка.png", currentObjectId, objects);

        Assert.Equal(currentObjectId, reference.OwnerId);
        Assert.Equal("улыбка.png", reference.FileName);
    }

    [Fact]
    public void NamedImageReferenceResolvesObjectWithoutChangingFilename()
    {
        var player = new IskraObject { Name = "Игрок" };
        var currentObjectId = Guid.NewGuid();

        var reference = ImageCommandSyntax.ResolveReference("игрок. улыбка.png", currentObjectId, [player]);

        Assert.Equal(player.Id, reference.OwnerId);
        Assert.Equal("улыбка.png", reference.FileName);
    }

    [Fact]
    public void UnknownImagePrefixRemainsPartOfLocalFilename()
    {
        var currentObjectId = Guid.NewGuid();

        var reference = ImageCommandSyntax.ResolveReference("папка.образ.png", currentObjectId, []);

        Assert.Equal(currentObjectId, reference.OwnerId);
        Assert.Equal("папка.образ.png", reference.FileName);
    }

    [Fact]
    public void ExactLocalFilenameTakesPriorityOverObjectNamePrefix()
    {
        var player = new IskraObject { Name = "Игрок" };
        var currentObjectId = Guid.NewGuid();

        var reference = ImageCommandSyntax.ResolveReference("игрок.png", currentObjectId, [player],
            fileName => fileName.Equals("игрок.png", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(currentObjectId, reference.OwnerId);
        Assert.Equal("игрок.png", reference.FileName);
    }

    [Fact]
    public void ExactLocalImageAssignmentDoesNotTargetObjectNamedLikeItsPrefix()
    {
        var player = new IskraObject { Name = "Игрок" };
        var currentObjectId = Guid.NewGuid();

        var assignment = ImageCommandSyntax.ResolveAssignment("Игрок.png", currentObjectId, [player],
            fileName => fileName.Equals("Игрок.png", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(currentObjectId, assignment.TargetId);
        Assert.Equal(currentObjectId, assignment.ResourceOwnerId);
        Assert.Equal("Игрок.png", assignment.FileName);
    }

    [Fact]
    public void NamedImageSourceStillChangesTheCurrentObject()
    {
        var player = new IskraObject { Name = "Игрок" };
        var currentObjectId = Guid.NewGuid();

        var assignment = ImageCommandSyntax.ResolveAssignment("игрок.улыбка.png", currentObjectId, [player]);

        Assert.Equal(currentObjectId, assignment.TargetId);
        Assert.Equal(player.Id, assignment.ResourceOwnerId);
        Assert.Equal("улыбка.png", assignment.FileName);
    }

    [Fact]
    public void RepeatingObjectNameAssignsItsOwnImageToThatObject()
    {
        var player = new IskraObject { Name = "Игрок" };
        var currentObjectId = Guid.NewGuid();

        var assignment = ImageCommandSyntax.ResolveAssignment("игрок. игрок.png", currentObjectId, [player]);

        Assert.Equal(player.Id, assignment.TargetId);
        Assert.Equal(player.Id, assignment.ResourceOwnerId);
        Assert.Equal("игрок.png", assignment.FileName);
    }

    [Fact]
    public void UnspacedObjectReferenceKeepsTheCurrentObjectAsDestination()
    {
        var player = new IskraObject { Name = "Игрок" };
        var currentObjectId = Guid.NewGuid();

        var assignment = ImageCommandSyntax.ResolveAssignment("игрок.игрок.png", currentObjectId, [player]);

        Assert.Equal(currentObjectId, assignment.TargetId);
        Assert.Equal(player.Id, assignment.ResourceOwnerId);
        Assert.Equal("игрок.png", assignment.FileName);
    }

    [Fact]
    public void WhitespaceAfterObjectSeparatorAssignsNamedObjectsImageToThatObject()
    {
        var target = new IskraObject { Name = "ы" };
        var currentObjectId = Guid.NewGuid();

        var assignment = ImageCommandSyntax.ResolveAssignment("ы. ы2.png", currentObjectId, [target]);

        Assert.Equal(target.Id, assignment.TargetId);
        Assert.Equal(target.Id, assignment.ResourceOwnerId);
        Assert.Equal("ы2.png", assignment.FileName);
    }

    [Theory]
    [InlineData("Изменить изображение. перед", null, "перед")]
    [InlineData("ИЗМЕНИТЬ ИЗОБРАЖЕНИЕ. НАЗАД", null, "назад")]
    [InlineData("Изменить изображение. игрок.перед", "игрок", "перед")]
    [InlineData("Изменить изображение. игрок . НАЗАД", "игрок", "назад")]
    public void StepCommandParsesCurrentOrNamedObject(string source, string? expectedObject, string expectedDirection)
    {
        Assert.True(ImageCommandSyntax.TryParseStepCommand(source, out var objectName, out var direction));

        Assert.Equal(expectedObject, objectName);
        Assert.Equal(expectedDirection, direction);
    }

    [Theory]
    [InlineData("Изменить изображение. боком")]
    [InlineData("Изменить изображение. .перед")]
    [InlineData("Печать привет")]
    public void InvalidOrUnrelatedStepCommandIsRejected(string source)
    {
        Assert.False(ImageCommandSyntax.TryParseStepCommand(source, out _, out _));
    }
}
