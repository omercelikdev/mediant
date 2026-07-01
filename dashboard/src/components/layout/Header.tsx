import { useState, useEffect, useCallback } from "react";
import { useTranslation } from "react-i18next";
import { Moon, Sun, Globe, Menu, X, ChevronDown } from "lucide-react";
import { cn } from "@/lib/utils";
import { languages } from "@/i18n";
import { useScrollspy } from "@/hooks/useScrollspy";

interface HeaderProps {
  theme: "light" | "dark";
  onToggleTheme: () => void;
}

const NAV_IDS = ["features", "benchmarks", "pipeline", "comparison", "quickstart"];

export function Header({ theme, onToggleTheme }: HeaderProps) {
  const { t, i18n } = useTranslation();
  const [scrolled, setScrolled] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [langOpen, setLangOpen] = useState(false);
  const activeId = useScrollspy(NAV_IDS, 120);

  useEffect(() => {
    const handler = () => setScrolled(window.scrollY > 20);
    handler();
    window.addEventListener("scroll", handler, { passive: true });
    return () => window.removeEventListener("scroll", handler);
  }, []);

  const scrollTo = useCallback((id: string) => {
    const el = document.getElementById(id);
    if (el) {
      el.scrollIntoView({ behavior: "smooth", block: "start" });
      setMobileOpen(false);
    }
  }, []);

  const changeLang = useCallback(
    (code: string) => {
      i18n.changeLanguage(code);
      setLangOpen(false);
    },
    [i18n],
  );

  const currentLang = languages.find((l) => l.code === i18n.language) ?? languages[0];

  return (
    <header
      className={cn(
        "fixed top-0 inset-x-0 z-50 transition-all duration-500",
        scrolled || mobileOpen
          ? "glass shadow-sm"
          : "bg-transparent",
        mobileOpen && "bg-surface dark:bg-surface",
      )}
    >
      <nav className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <div className="flex h-16 items-center justify-between">
          {/* Text Logo */}
          <button
            onClick={() => window.scrollTo({ top: 0, behavior: "smooth" })}
            className="flex items-center gap-2 cursor-pointer"
          >
            <span className="text-xl font-bold tracking-tight">
              <span className="gradient-text">Mediant</span>
              <span className="text-text-primary"> Mediator</span>
            </span>
          </button>

          {/* Desktop Nav */}
          <div className="hidden md:flex items-center gap-1">
            {NAV_IDS.map((id) => (
              <button
                key={id}
                onClick={() => scrollTo(id)}
                className={cn(
                  "px-3 py-2 text-sm font-medium rounded-lg transition-colors cursor-pointer",
                  activeId === id
                    ? "text-brand-600 dark:text-brand-400 bg-brand-50 dark:bg-brand-950"
                    : "text-text-secondary hover:text-text-primary hover:bg-surface-hover",
                )}
              >
                {t(`nav.${id === "quickstart" ? "quickStart" : id}`)}
              </button>
            ))}
          </div>

          {/* Controls */}
          <div className="flex items-center gap-2">
            {/* Language Selector */}
            <div className="relative">
              <button
                onClick={() => setLangOpen(!langOpen)}
                className="flex items-center gap-1.5 px-2.5 py-2 text-sm text-text-secondary hover:text-text-primary rounded-lg hover:bg-surface-hover transition-colors cursor-pointer"
                aria-label="Select language"
              >
                <Globe className="w-4 h-4" />
                <span className="hidden sm:inline">{currentLang.name}</span>
                <ChevronDown className={cn("w-3 h-3 transition-transform", langOpen && "rotate-180")} />
              </button>
              {langOpen && (
                <>
                  <div
                    className="fixed inset-0 z-40"
                    onClick={() => setLangOpen(false)}
                  />
                  <div className="absolute end-0 top-full mt-1 z-50 w-44 rounded-xl border border-border bg-surface shadow-xl shadow-black/10 py-1 overflow-hidden">
                    {languages.map((lang) => (
                      <button
                        key={lang.code}
                        onClick={() => changeLang(lang.code)}
                        className={cn(
                          "w-full text-start px-4 py-2.5 text-sm transition-colors cursor-pointer",
                          i18n.language === lang.code
                            ? "text-brand-600 dark:text-brand-400 bg-brand-50 dark:bg-brand-950 font-medium"
                            : "text-text-secondary hover:text-text-primary hover:bg-surface-hover",
                        )}
                      >
                        {lang.name}
                      </button>
                    ))}
                  </div>
                </>
              )}
            </div>

            {/* Theme Toggle */}
            <button
              onClick={onToggleTheme}
              className="p-2 text-text-secondary hover:text-text-primary rounded-lg hover:bg-surface-hover transition-colors cursor-pointer"
              aria-label={t(`theme.${theme === "dark" ? "light" : "dark"}`)}
            >
              {theme === "dark" ? (
                <Sun className="w-4.5 h-4.5" />
              ) : (
                <Moon className="w-4.5 h-4.5" />
              )}
            </button>

            {/* Mobile Menu Toggle */}
            <button
              onClick={() => setMobileOpen(!mobileOpen)}
              className="md:hidden p-2 text-text-secondary hover:text-text-primary rounded-lg hover:bg-surface-hover transition-colors cursor-pointer"
              aria-label="Toggle menu"
            >
              {mobileOpen ? (
                <X className="w-5 h-5" />
              ) : (
                <Menu className="w-5 h-5" />
              )}
            </button>
          </div>
        </div>

        {/* Mobile Nav */}
        {mobileOpen && (
          <>
          <div className="fixed inset-0 top-16 bg-black/40 z-40 md:hidden" onClick={() => setMobileOpen(false)} />
          <div className="md:hidden pb-6 border-t border-border mt-2 pt-4 space-y-1 relative z-50 bg-surface dark:bg-surface shadow-xl rounded-b-2xl">
            {NAV_IDS.map((id) => (
              <button
                key={id}
                onClick={() => scrollTo(id)}
                className={cn(
                  "block w-full text-start px-4 py-2.5 text-sm font-medium rounded-lg transition-colors cursor-pointer",
                  activeId === id
                    ? "text-brand-600 dark:text-brand-400 bg-brand-50 dark:bg-brand-950"
                    : "text-text-secondary hover:text-text-primary hover:bg-surface-hover",
                )}
              >
                {t(`nav.${id === "quickstart" ? "quickStart" : id}`)}
              </button>
            ))}
          </div>
          </>
        )}
      </nav>
    </header>
  );
}
