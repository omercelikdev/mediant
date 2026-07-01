using FluentValidation;
using Mediant.Sample.ECommerce.Commands;

namespace Mediant.Sample.ECommerce.Validators;

public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required");

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("At least one item is required");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.ProductId).NotEmpty();
            item.RuleFor(x => x.Quantity).GreaterThan(0);
            item.RuleFor(x => x.UnitPrice).GreaterThan(0);
        });
    }
}

public sealed class ProcessPaymentValidator : AbstractValidator<ProcessPaymentCommand>
{
    public ProcessPaymentValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("OrderId is required");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount must be positive");

        RuleFor(x => x.CardNumber)
            .NotEmpty().WithMessage("CardNumber is required")
            .CreditCard().WithMessage("Invalid card number");
    }
}
