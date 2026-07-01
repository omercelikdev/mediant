export type ComparisonValue = "yes" | "no" | "partial" | string;

export interface ComparisonRow {
  key: string;
  mediant: ComparisonValue;
  mediatr: ComparisonValue;
  mediantHighlight: boolean;
}

export const comparisonData: ComparisonRow[] = [
  {
    key: "license",
    mediant: "mitLicense",
    mediatr: "apacheLicense",
    mediantHighlight: false,
  },
  {
    key: "cqrsTypes",
    mediant: "yes",
    mediatr: "no",
    mediantHighlight: true,
  },
  {
    key: "resultPattern",
    mediant: "yes",
    mediatr: "no",
    mediantHighlight: true,
  },
  {
    key: "pipelineBehaviors",
    mediant: "eleven",
    mediatr: "custom",
    mediantHighlight: true,
  },
  {
    key: "httpEndpoints",
    mediant: "yes",
    mediatr: "no",
    mediantHighlight: true,
  },
  {
    key: "streaming",
    mediant: "yes",
    mediatr: "partial",
    mediantHighlight: true,
  },
  {
    key: "performance",
    mediant: "faster",
    mediatr: "baseline",
    mediantHighlight: true,
  },
  {
    key: "domainEvents",
    mediant: "builtIn",
    mediatr: "notification",
    mediantHighlight: true,
  },
  {
    key: "fluentValidation",
    mediant: "native",
    mediatr: "thirdParty",
    mediantHighlight: true,
  },
];
