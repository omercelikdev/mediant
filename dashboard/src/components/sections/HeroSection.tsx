import { useTranslation } from "react-i18next";
import { motion } from "framer-motion";
import { ArrowRight, Terminal, Zap, Cpu, Layers, FlaskConical } from "lucide-react";

function GithubIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="currentColor">
      <path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/>
    </svg>
  );
}
import { Button } from "@/components/ui/Button";
import { Badge } from "@/components/ui/Badge";
import { CodeBlock } from "@/components/ui/CodeBlock";
import { heroStats, APP_VERSION } from "@/data/config";

const stats = [
  { key: "fasterPublish", value: heroStats.fasterPublish, icon: Zap },
  { key: "lessMemory", value: heroStats.lessMemory, icon: Cpu },
  { key: "pipelineBehaviors", value: heroStats.pipelineBehaviors, icon: Layers },
  { key: "testsCoverage", value: heroStats.testsCoverage, icon: FlaskConical },
];

export function HeroSection() {
  const { t } = useTranslation();

  return (
    <section className="relative min-h-screen flex items-center overflow-hidden pt-16 w-full">
      {/* Background decoration */}
      <div className="absolute inset-0 -z-10">
        <div className="absolute top-0 start-1/4 w-96 h-96 bg-brand-500/10 rounded-full blur-3xl" />
        <div className="absolute bottom-1/4 end-1/4 w-80 h-80 bg-purple-500/10 rounded-full blur-3xl" />
        <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,transparent_0%,var(--color-surface)_70%)]" />
      </div>

      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 py-20 sm:py-32 overflow-hidden">
        <div className="grid lg:grid-cols-2 gap-12 lg:gap-16 items-center min-w-0">
          {/* Left Content */}
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.6 }}
            className="min-w-0"
          >
            <Badge variant="brand" className="mb-6">
              <span className="w-1.5 h-1.5 rounded-full bg-brand-500 animate-pulse" />
              v{APP_VERSION} {t("hero.badge")}
            </Badge>

            <h1 className="text-3xl sm:text-5xl lg:text-6xl font-bold tracking-tight leading-[1.1] mb-6 break-words">
              {t("hero.title")}{" "}
              <span className="gradient-text">{t("hero.titleHighlight")}</span>
            </h1>

            <p className="text-base sm:text-xl text-text-secondary leading-relaxed mb-8 max-w-xl">
              {t("hero.description")}
            </p>

            <div className="flex flex-col sm:flex-row gap-3 sm:gap-4 mb-10 sm:items-center">
              <Button
                size="lg"
                className="w-full sm:w-auto"
                onClick={() =>
                  document.getElementById("quickstart")?.scrollIntoView({ behavior: "smooth" })
                }
              >
                {t("hero.getStarted")}
                <ArrowRight className="w-4 h-4" />
              </Button>
              <Button
                variant="secondary"
                size="lg"
                className="w-full sm:w-auto"
                onClick={() =>
                  window.open("https://github.com/omercelikdev/mediant", "_blank")
                }
              >
                <GithubIcon className="w-4 h-4" />
                {t("hero.viewOnGithub")}
              </Button>
            </div>

            {/* Install Command */}
            <div className="flex items-center gap-3 px-4 py-3 rounded-xl bg-[#0d1117] dark:bg-[#0a0e17] border border-border max-w-md overflow-x-auto">
              <Terminal className="w-4 h-4 text-brand-400 shrink-0" />
              <code className="text-sm text-slate-300 font-mono">
                {t("hero.installCommand")}
              </code>
            </div>
          </motion.div>

          {/* Right - Stats Cards */}
          <motion.div
            initial={{ opacity: 0, y: 30 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.6, delay: 0.2 }}
            className="grid grid-cols-2 gap-4"
          >
            {stats.map((stat, i) => (
              <motion.div
                key={stat.key}
                initial={{ opacity: 0, scale: 0.9 }}
                animate={{ opacity: 1, scale: 1 }}
                transition={{ duration: 0.4, delay: 0.3 + i * 0.1 }}
                className="group relative rounded-2xl border border-border bg-surface-alt p-6 hover:shadow-lg hover:shadow-brand-500/5 hover:-translate-y-1 hover:border-brand-200 dark:hover:border-brand-800 transition-all duration-300"
              >
                <div className="flex items-center gap-3 mb-3">
                  <div className="p-2 rounded-lg bg-brand-500/10">
                    <stat.icon className="w-5 h-5 text-brand-600 dark:text-brand-400" />
                  </div>
                </div>
                <div className="text-3xl sm:text-4xl font-bold text-text-primary mb-1">
                  {stat.value}
                </div>
                <div className="text-sm text-text-secondary">
                  {t(`stats.${stat.key}`)}
                </div>
              </motion.div>
            ))}
          </motion.div>
        </div>

        {/* Code Preview */}
        <motion.div
          initial={{ opacity: 0, y: 40 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.6, delay: 0.5 }}
          className="mt-20"
        >
          <CodeBlock
            language="csharp"
            code={`// Define a command with CQRS
public record CreateOrderCommand(string Product, int Qty) : ICommand<Result<Guid>>;

// Handle with pipeline behaviors
[Auditable, Transactional, Retryable(3)]
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        CreateOrderCommand command, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        return Result<Guid>.Success(id);
    }
}`}
          />
        </motion.div>
      </div>
    </section>
  );
}
