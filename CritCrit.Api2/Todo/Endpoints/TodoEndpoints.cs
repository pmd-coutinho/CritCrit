using CritCrit.Api2.Infrastructure.Email;
using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Persistence;

namespace CritCrit.Api2.Todo.Endpoints;

[Tags("Todos")]
public static class TodoEndpoints
{
    public sealed record CreateTodoRequest(string Name, string Description);
    
    public class CreateTodoRequestValidator : AbstractValidator<CreateTodoRequest>
    {
        public CreateTodoRequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
            RuleFor(x => x.Description).NotEmpty();
        }
    }

    [WolverinePost("api/todos")]
    [ProducesResponseType(typeof(Events.TodoCreated), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public static async Task<(CreationResponse<Events.TodoCreated>, TodoCreatedEmail)> CreateTodoAsync(
        [FromBody] CreateTodoRequest request, IDocumentSession session)
    {
        var todo = new Events.TodoCreated(Guid.CreateVersion7(), request.Name, request.Description, DateTime.UtcNow);
        session.Events.StartStream<Domain.Todo>(todo.Id, todo);
        await session.SaveChangesAsync();
        return (new CreationResponse<Events.TodoCreated>($"/api/todos/{todo.Id}", todo), new TodoCreatedEmail(todo));
    }

    [WolverineGet("api/todos/{todoId}")]
    [ProducesResponseType(typeof(Domain.Todo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(void), StatusCodes.Status404NotFound)]
    
    public static IResult GetTodo([Entity]Domain.Todo todo)
    {
        return Results.Ok(todo);
    }

    [WolverinePatch("api/todos/{todoId}/complete")]
    [ProducesResponseType(typeof(void), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(void), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public static async Task<IResult> CompleteTodo(Guid todoId, IDocumentSession session)
    {
        var @event = new Events.TodoCompleted();
        var todo = await session.Events.FetchForWriting<Domain.Todo>(todoId);
        if (todo.Aggregate is null)
            return Results.NotFound();
        
        if (todo.Aggregate.IsComplete)
            return Results.Conflict(new ProblemDetails
            {
                Title = "Todo already completed",
                Status = StatusCodes.Status409Conflict
            });
        
        todo.AppendOne(@event);
        await session.SaveChangesAsync();

        return Results.Ok();
    }
}

public class TodoCreatedEmail : EmailMessage
{
    public Events.TodoCreated Todo { get; set; }

    public TodoCreatedEmail(Events.TodoCreated todo)
    {
        Todo = todo;
        To = "pmd.coutinho@gmail.com";
        Subject = $"Todo Created: {todo.Name}";
        Body = $"A new todo has been created with the following details:\n\n" +
               $"ID: {todo.Id}\n" +
               $"Name: {todo.Name}\n" +
               $"Description: {todo.Description}\n" +
               $"Created At: {todo.CreatedAt}";
    }
}

public static class TodoCreatedEmailMessageHandler
{
    public static async Task HandleAsync(TodoCreatedEmail email, IEmailSender emailSender, CancellationToken ct)
    {
        await emailSender.SendMessageAsync(email, ct);
    }
}