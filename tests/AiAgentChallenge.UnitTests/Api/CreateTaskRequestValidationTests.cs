using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Infrastructure.Orchestration;

namespace AiAgentChallenge.UnitTests.Api;

public sealed class CreateTaskRequestValidationTests
{
    [Fact]
    public void Validate_ReturnsErrors_ForWhitespaceFields()
    {
        var validator = new TaskSubmissionRequestValidator();
        var request = new TaskSubmissionRequest
        {
            TaskId = " ",
            Title = " ",
            Description = " "
        };

        var errors = validator.Validate(request);

        Assert.Contains(nameof(TaskSubmissionRequest.TaskId), errors.Keys);
        Assert.Contains(nameof(TaskSubmissionRequest.Title), errors.Keys);
        Assert.Contains(nameof(TaskSubmissionRequest.Description), errors.Keys);
    }
}
