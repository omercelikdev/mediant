import { useTranslation } from "react-i18next";
import { motion } from "framer-motion";
import { Check, X, Minus } from "lucide-react";
import { SectionHeading } from "@/components/ui/SectionHeading";
import { comparisonData } from "@/data/comparison";
import { cn } from "@/lib/utils";

function ValueCell({ value, highlight }: { value: string; highlight: boolean }) {
  const { t } = useTranslation();
  const resolved = t(`comparison.values.${value}`, { defaultValue: value });

  if (value === "yes") {
    return (
      <span className={cn("inline-flex items-center gap-1.5 text-sm font-medium", highlight ? "text-success" : "text-success")}>
        <Check className="w-4 h-4" />
        {resolved}
      </span>
    );
  }
  if (value === "no") {
    return (
      <span className="inline-flex items-center gap-1.5 text-sm text-text-tertiary">
        <X className="w-4 h-4" />
        {resolved}
      </span>
    );
  }
  if (value === "partial") {
    return (
      <span className="inline-flex items-center gap-1.5 text-sm text-warning">
        <Minus className="w-4 h-4" />
        {resolved}
      </span>
    );
  }

  return (
    <span className={cn("text-sm", highlight ? "font-medium text-brand-600 dark:text-brand-400" : "text-text-secondary")}>
      {resolved}
    </span>
  );
}

export function ComparisonSection() {
  const { t } = useTranslation();

  return (
    <section id="comparison" className="py-24 sm:py-32 bg-surface-alt">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <SectionHeading
          title={t("comparison.title")}
          subtitle={t("comparison.subtitle")}
        />

        <motion.div
          initial={{ opacity: 0, y: 20 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true }}
          transition={{ duration: 0.5 }}
          className="max-w-4xl mx-auto overflow-x-auto"
        >
          <div className="rounded-2xl border border-border bg-surface overflow-hidden min-w-[540px]">
            {/* Header */}
            <div className="grid grid-cols-3 bg-surface-hover">
              <div className="px-6 py-4 text-sm font-semibold text-text-primary">
                {t("comparison.feature")}
              </div>
              <div className="px-6 py-4 text-sm font-semibold text-brand-600 dark:text-brand-400 text-center">
                {t("comparison.mediant")}
              </div>
              <div className="px-6 py-4 text-sm font-semibold text-text-secondary text-center">
                {t("comparison.mediatr")}
              </div>
            </div>

            {/* Rows */}
            {comparisonData.map((row, i) => (
              <div
                key={row.key}
                className={cn(
                  "grid grid-cols-3 border-t border-border transition-colors hover:bg-surface-hover",
                  i % 2 === 0 ? "bg-surface" : "bg-surface-alt/50",
                )}
              >
                <div className="px-6 py-4 text-sm text-text-primary font-medium">
                  {t(`comparison.items.${row.key}`)}
                </div>
                <div className="px-6 py-4 text-center">
                  <ValueCell value={row.mediant} highlight={row.mediantHighlight} />
                </div>
                <div className="px-6 py-4 text-center">
                  <ValueCell value={row.mediatr} highlight={false} />
                </div>
              </div>
            ))}
          </div>
        </motion.div>
      </div>
    </section>
  );
}
