import { useTranslation } from "react-i18next";
import { ExternalLink } from "lucide-react";

function GithubIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="currentColor">
      <path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/>
    </svg>
  );
}

export function Footer() {
  const { t } = useTranslation();
  const year = new Date().getFullYear();

  return (
    <footer className="border-t border-border bg-surface-alt">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 py-16">
        <div className="grid sm:grid-cols-2 lg:grid-cols-4 gap-10">
          {/* Brand */}
          <div className="sm:col-span-2 lg:col-span-1">
            <span className="text-lg font-bold tracking-tight">
              <span className="gradient-text">Qorpe</span>
              <span className="text-text-primary"> Mediator</span>
            </span>
            <p className="text-sm text-text-secondary mt-3 leading-relaxed max-w-xs">
              {t("footer.description")}
            </p>
            <a
              href="https://github.com/qorpe/mediator"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 mt-4 text-sm text-text-secondary hover:text-text-primary transition-colors"
            >
              <GithubIcon className="w-4 h-4" />
              GitHub
              <ExternalLink className="w-3 h-3" />
            </a>
          </div>

          {/* Resources */}
          <div>
            <h4 className="text-sm font-semibold text-text-primary mb-4">
              {t("footer.resources")}
            </h4>
            <ul className="space-y-3">
              {[
                { label: t("footer.documentation"), href: "https://github.com/qorpe/mediator/blob/main/docs/README.md" },
                { label: t("footer.migrationGuide"), href: "https://github.com/qorpe/mediator/blob/main/docs/MIGRATION_GUIDE.md" },
                { label: t("footer.changelog"), href: "https://github.com/qorpe/mediator/blob/main/docs/CHANGELOG.md" },
              ].map((link) => (
                <li key={link.label}>
                  <a
                    href={link.href}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-sm text-text-secondary hover:text-text-primary transition-colors"
                  >
                    {link.label}
                  </a>
                </li>
              ))}
            </ul>
          </div>

          {/* Community */}
          <div>
            <h4 className="text-sm font-semibold text-text-primary mb-4">
              {t("footer.community")}
            </h4>
            <ul className="space-y-3">
              {[
                { label: t("footer.github"), href: "https://github.com/qorpe/mediator" },
                { label: t("footer.issues"), href: "https://github.com/qorpe/mediator/issues" },
                { label: t("footer.discussions"), href: "https://github.com/qorpe/mediator/discussions" },
                { label: t("footer.nuget"), href: "https://www.nuget.org/packages/Mediant" },
              ].map((link) => (
                <li key={link.label}>
                  <a
                    href={link.href}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-sm text-text-secondary hover:text-text-primary transition-colors"
                  >
                    {link.label}
                  </a>
                </li>
              ))}
            </ul>
          </div>

          {/* Legal */}
          <div>
            <h4 className="text-sm font-semibold text-text-primary mb-4">
              {t("footer.legal")}
            </h4>
            <ul className="space-y-3">
              <li>
                <a
                  href="https://github.com/qorpe/mediator/blob/main/LICENSE"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-sm text-text-secondary hover:text-text-primary transition-colors"
                >
                  {t("footer.mitLicense")}
                </a>
              </li>
            </ul>
          </div>
        </div>

        <div className="mt-12 pt-8 border-t border-border flex flex-col sm:flex-row items-center justify-between gap-4">
          <p className="text-xs text-text-tertiary">
            &copy; {year} Qorpe. {t("footer.rights")}
          </p>
        </div>
      </div>
    </footer>
  );
}
