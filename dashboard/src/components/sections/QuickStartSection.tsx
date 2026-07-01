import { useTranslation } from "react-i18next";
import { motion } from "framer-motion";
import { SectionHeading } from "@/components/ui/SectionHeading";
import { CodeBlock } from "@/components/ui/CodeBlock";

const steps = [
  {
    key: "step1",
    language: "bash",
    code: `dotnet add package Mediant
dotnet add package Mediant.Behaviors
dotnet add package Mediant.FluentValidation`,
  },
  {
    key: "step2",
    language: "csharp",
    code: `builder.Services.AddQorpeMediator(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddBehaviors();
    cfg.AddFluentValidation();
});`,
  },
  {
    key: "step3",
    language: "csharp",
    code: `[Auditable, Transactional]
[HttpEndpoint(HttpMethod.Post, "/api/orders", Summary = "Create order")]
public record CreateOrderCommand(
    string ProductName,
    int Quantity
) : ICommand<Result<Guid>>;`,
  },
  {
    key: "step4",
    language: "csharp",
    code: `public class CreateOrderHandler
    : ICommandHandler<CreateOrderCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        CreateOrderCommand command, CancellationToken ct)
    {
        var orderId = Guid.NewGuid();
        // ... your business logic
        return Result<Guid>.Success(orderId);
    }
}

// Dispatch from anywhere
var result = await mediator.Send(new CreateOrderCommand("Widget", 5));
result.Match(
    id => Console.WriteLine($"Created: {id}"),
    error => Console.WriteLine($"Failed: {error.Description}")
);`,
  },
];

export function QuickStartSection() {
  const { t } = useTranslation();

  return (
    <section id="quickstart" className="py-24 sm:py-32">
      <div className="mx-auto max-w-4xl px-4 sm:px-6 lg:px-8">
        <SectionHeading
          title={t("quickStart.title")}
          subtitle={t("quickStart.subtitle")}
        />

        <div className="space-y-8">
          {steps.map((step, i) => (
            <motion.div
              key={step.key}
              initial={{ opacity: 0, y: 20 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true, margin: "-30px" }}
              transition={{ duration: 0.4, delay: i * 0.1 }}
            >
              <div className="flex items-center gap-3 mb-3">
                <span className="flex items-center justify-center w-8 h-8 rounded-lg bg-brand-500/10 text-brand-600 dark:text-brand-400 text-sm font-bold">
                  {i + 1}
                </span>
                <h3 className="text-base font-semibold text-text-primary">
                  {t(`quickStart.${step.key}`)}
                </h3>
              </div>
              <CodeBlock code={step.code} language={step.language} />
            </motion.div>
          ))}
        </div>
      </div>
    </section>
  );
}
