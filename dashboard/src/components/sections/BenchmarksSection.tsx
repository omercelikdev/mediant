import { useState } from "react";
import { useTranslation } from "react-i18next";
import { motion } from "framer-motion";
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  ResponsiveContainer,
  CartesianGrid,
  Cell,
} from "recharts";
import { SectionHeading } from "@/components/ui/SectionHeading";
import { Card } from "@/components/ui/Card";
import { sendBenchmarks, publishBenchmarks, memoryBenchmarks } from "@/data/benchmarks";
import { benchmarkHighlights } from "@/data/config";

type Tab = "send" | "publish" | "memory";

const tabs: { key: Tab; labelKey: string }[] = [
  { key: "send", labelKey: "benchmarks.sendPerformance" },
  { key: "publish", labelKey: "benchmarks.publishPerformance" },
  { key: "memory", labelKey: "benchmarks.memoryUsage" },
];

const dataMap: Record<Tab, typeof sendBenchmarks> = {
  send: sendBenchmarks,
  publish: publishBenchmarks,
  memory: memoryBenchmarks,
};

const MEDIANT_COLOR = "#3b82f6";
const MEDIATR_COLOR = "#94a3b8";

interface TooltipPayloadItem {
  name: string;
  value: number;
  color: string;
}

function CustomTooltip({
  active,
  payload,
  label,
  unit,
}: {
  active?: boolean;
  payload?: TooltipPayloadItem[];
  label?: string;
  unit: string;
}) {
  if (!active || !payload) return null;
  return (
    <div className="rounded-xl border border-border bg-surface p-3 shadow-xl text-sm">
      <p className="font-medium text-text-primary mb-2">{label}</p>
      {payload.map((entry) => (
        <div key={entry.name} className="flex items-center gap-2 py-0.5">
          <span
            className="w-2.5 h-2.5 rounded-full"
            style={{ backgroundColor: entry.color }}
          />
          <span className="text-text-secondary">{entry.name}:</span>
          <span className="font-medium text-text-primary">
            {entry.value.toLocaleString()} {unit}
          </span>
        </div>
      ))}
    </div>
  );
}

export function BenchmarksSection() {
  const { t } = useTranslation();
  const [activeTab, setActiveTab] = useState<Tab>("publish");

  const data = dataMap[activeTab];
  const unit = activeTab === "memory" ? "B" : "ns";

  return (
    <section id="benchmarks" className="py-24 sm:py-32 bg-surface-alt">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <SectionHeading
          title={t("benchmarks.title")}
          subtitle={t("benchmarks.subtitle")}
        />

        {/* Tabs */}
        <div className="flex justify-center mb-10">
          <div className="inline-flex rounded-xl bg-surface border border-border p-1">
            {tabs.map((tab) => (
              <button
                key={tab.key}
                onClick={() => setActiveTab(tab.key)}
                className={`px-4 py-2 text-sm font-medium rounded-lg transition-all cursor-pointer ${
                  activeTab === tab.key
                    ? "bg-brand-600 text-white shadow-md"
                    : "text-text-secondary hover:text-text-primary"
                }`}
              >
                {t(tab.labelKey)}
              </button>
            ))}
          </div>
        </div>

        <motion.div
          key={activeTab}
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.3 }}
        >
          <Card className="p-4 sm:p-8">
            <ResponsiveContainer width="100%" height={400}>
              <BarChart
                data={data}
                barCategoryGap="20%"
                barGap={8}
              >
                <CartesianGrid
                  strokeDasharray="3 3"
                  vertical={false}
                  stroke="var(--color-border)"
                />
                <XAxis
                  dataKey="name"
                  tick={{ fill: "var(--color-text-secondary)", fontSize: 12 }}
                  axisLine={{ stroke: "var(--color-border)" }}
                  tickLine={false}
                />
                <YAxis
                  tick={{ fill: "var(--color-text-secondary)", fontSize: 12 }}
                  axisLine={false}
                  tickLine={false}
                  tickFormatter={(v: number) => `${v.toLocaleString()}`}
                />
                <Tooltip
                  content={<CustomTooltip unit={unit} />}
                  cursor={{ fill: "var(--color-surface-hover)", radius: 8 }}
                />
                <Bar
                  dataKey="mediant"
                  name={t("benchmarks.mediant")}
                  radius={[8, 8, 0, 0]}
                  maxBarSize={60}
                >
                  {data.map((_, index) => (
                    <Cell key={`q-${index}`} fill={MEDIANT_COLOR} />
                  ))}
                </Bar>
                <Bar
                  dataKey="mediatr"
                  name={t("benchmarks.mediatr")}
                  radius={[8, 8, 0, 0]}
                  maxBarSize={60}
                >
                  {data.map((_, index) => (
                    <Cell key={`m-${index}`} fill={MEDIATR_COLOR} />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>

            {/* Legend */}
            <div className="flex justify-center gap-8 mt-6 pt-6 border-t border-border">
              <div className="flex items-center gap-2">
                <span className="w-3 h-3 rounded-full" style={{ backgroundColor: MEDIANT_COLOR }} />
                <span className="text-sm font-medium text-text-primary">Mediant Mediator</span>
              </div>
              <div className="flex items-center gap-2">
                <span className="w-3 h-3 rounded-full" style={{ backgroundColor: MEDIATR_COLOR }} />
                <span className="text-sm font-medium text-text-secondary">MediatR v12</span>
              </div>
            </div>
          </Card>

          {/* Highlight cards */}
          <div className="grid sm:grid-cols-3 gap-4 mt-6">
            <Card variant="glass" className="text-center">
              <div className="text-2xl font-bold text-brand-600 dark:text-brand-400">{benchmarkHighlights.fasterPercent}</div>
              <div className="text-sm text-text-secondary mt-1">{t("benchmarks.faster")}</div>
            </Card>
            <Card variant="glass" className="text-center">
              <div className="text-2xl font-bold text-brand-600 dark:text-brand-400">{benchmarkHighlights.lessMemoryMultiplier}</div>
              <div className="text-sm text-text-secondary mt-1">{t("benchmarks.lessMemory")}</div>
            </Card>
            <Card variant="glass" className="text-center">
              <div className="text-2xl font-bold text-brand-600 dark:text-brand-400">{benchmarkHighlights.publishHotPath}</div>
              <div className="text-sm text-text-secondary mt-1">Publish Hot Path</div>
            </Card>
          </div>
        </motion.div>
      </div>
    </section>
  );
}
