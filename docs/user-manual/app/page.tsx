"use client";

import {
  manualArticles,
  manualCategories,
  manualSections,
  masterDataMatrix,
  type ManualArticle,
  type ManualAudience,
  type ManualCalloutTone,
} from "./manual-data";
import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";

const articles = manualArticles as readonly ManualArticle[];
const audienceOptions: readonly {
  id: "everything" | ManualAudience;
  label: string;
}[] = [
  { id: "everything", label: "All guides" },
  { id: "portal", label: "Portal" },
  { id: "mobile", label: "Android" },
  { id: "administrator", label: "Admin" },
];

function normalizeSearchText(value: string) {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, " ").trim();
}

function SearchIcon() {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <circle cx="11" cy="11" r="6.5" />
      <path d="m16 16 4 4" />
    </svg>
  );
}

function ArrowIcon() {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="M5 12h14M14 7l5 5-5 5" />
    </svg>
  );
}

function LogoMark() {
  return (
    <span className="logo-mark" aria-hidden="true">
      <span />
      <span />
      <span />
    </span>
  );
}

function Callout({
  tone,
  title,
  children,
}: {
  tone: ManualCalloutTone;
  title: string;
  children: ReactNode;
}) {
  const labels: Record<ManualCalloutTone, string> = {
    info: "Note",
    tip: "Field tip",
    warning: "Check",
    success: "Confirmed",
  };

  return (
    <aside className={`callout callout-${tone}`}>
      <div className="callout-label">
        <span>{labels[tone]}</span>
        <i aria-hidden="true" />
      </div>
      <div>
        <strong>{title}</strong>
        <p>{children}</p>
      </div>
    </aside>
  );
}

type ScreenshotReference = {
  src: string;
  alt: string;
  title: string;
  caption: string;
  kind: "portal" | "mobile";
  href?: string;
  linkLabel?: string;
};

const screenshotLibrary: Record<string, readonly ScreenshotReference[]> = {
  "portal-login": [{ src: "screenshots/portal-login.png", alt: "Portal sign-in screen", title: "Portal sign in", caption: "Sign in with your existing account.", kind: "portal" }],
  "master-data-services": [{ src: "screenshots/portal-service-create.png", alt: "Service catalog form", title: "Service catalog", caption: "Maintain services in Master Data using the existing catalog permissions.", kind: "portal" }],
};

function ScreenshotGallery({ article }: { article: ManualArticle }) {
  const screenshots =
    screenshotLibrary[article.screenshotKey ?? article.id] ?? [];

  return (
    <section className="screenshot-section" aria-labelledby="observed-interface-title">
      <div className="section-heading-row">
        <div>
          <span className="micro-label">Observed interface</span>
          <h2 id="observed-interface-title">Screen reference</h2>
        </div>
        <span className="privacy-note">Captured from the running applications</span>
      </div>
      <div className="screenshot-grid">
        {screenshots.map((shot) => (
          <figure className={`screenshot-card ${shot.kind}`} key={shot.src}>
            <div className="screenshot-browserbar">
              <span />
              <span />
              <span />
              <small>{shot.title}</small>
            </div>
            <div className="screenshot-viewport">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src={shot.src} alt={shot.alt} />
            </div>
            <figcaption>
              <span>{shot.caption}</span>
              {shot.href && (
                <a href={shot.href} target="_blank" rel="noreferrer">
                  {shot.linkLabel ?? "Open file"} <ArrowIcon />
                </a>
              )}
            </figcaption>
          </figure>
        ))}
      </div>
    </section>
  );
}

function RelevantMasterData({ articleId }: { articleId: string }) {
  const recordGroups: Record<string, readonly string[]> = {
    "master-countries": ["Countries"],
    "master-stations": ["Stations"],
    "master-manpower-licenses": ["Manpower types", "Licenses"],
    "master-services-operation-types": ["Services", "Operation types"],
    "master-aircraft-resources": [
      "Aircraft types",
      "Tools",
      "Materials",
      "General supports",
    ],
    "master-customers": ["Customers"],
    "master-staff": ["Staff"],
    "master-portal-access": ["Portal access"],
  };
  const wanted = recordGroups[articleId];
  const rows = wanted
    ? masterDataMatrix.filter((row) => wanted.includes(row.record))
    : masterDataMatrix;

  return (
    <section className="matrix-section" aria-labelledby="matrix-title">
      <div className="section-heading-row">
        <div>
          <span className="micro-label">Data map</span>
          <h2 id="matrix-title">What this catalog controls</h2>
        </div>
        <span className="visual-key">{rows.length} record {rows.length === 1 ? "type" : "types"}</span>
      </div>
      <div className="matrix-table-wrap">
        <table className="matrix-table">
          <thead>
            <tr>
              <th>Record</th>
              <th>Key fields</th>
              <th>Used by</th>
              <th>Controls</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id}>
                <th scope="row">
                  <strong>{row.record}</strong>
                  <small>{row.location}</small>
                </th>
                <td>{row.keyFields.join(" · ")}</td>
                <td>{row.usedBy.join(" · ")}</td>
                <td>{row.controls.join(" · ")}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function AudienceBadge({ audience }: { audience: ManualAudience }) {
  const labels: Record<ManualAudience, string> = {
    all: "All users",
    portal: "Portal",
    mobile: "Mobile",
    administrator: "Administrator",
  };
  return <span className={`audience-badge audience-${audience}`}>{labels[audience]}</span>;
}

function ArticleReader({
  article,
  onSelect,
}: {
  article: ManualArticle;
  onSelect: (id: string) => void;
}) {
  const section = manualSections.find((item) => item.id === article.sectionId);
  const category = manualCategories.find((item) => item.id === article.categoryId);
  const related = articles
    .filter((item) => item.sectionId === article.sectionId && item.id !== article.id)
    .slice(0, 3);
  const showScreens = (screenshotLibrary[article.screenshotKey ?? article.id]?.length ?? 0) > 0;

  return (
    <article className="article-reader">
      <header className="article-hero">
        <div className="article-breadcrumb">
          <span>{section?.title}</span>
          <i>/</i>
          <span>{category?.title}</span>
        </div>
        <div className="article-title-row">
          <div>
            <span className="article-eyebrow">{article.eyebrow}</span>
            <h1>{article.title}</h1>
          </div>
          <span className="article-number">
            {String(articles.findIndex((item) => item.id === article.id) + 1).padStart(2, "0")}
          </span>
        </div>
        <p className="article-summary">{article.summary}</p>
        <div className="article-meta">
          <AudienceBadge audience={article.audience} />
          {article.location && (
            <span className="location-chip">
              <span aria-hidden="true">⌁</span>
              {article.location}
            </span>
          )}
        </div>
      </header>

      <div className="article-layout">
        <div className="article-body">
          <section className="steps-section" aria-labelledby="procedure-title">
            <div className="section-heading-row">
              <div>
                <span className="micro-label">Procedure</span>
                <h2 id="procedure-title">Do this in order</h2>
              </div>
              <span className="step-count">{article.steps.length} steps</span>
            </div>
            <ol className="procedure-list">
              {article.steps.map((step, index) => (
                <li key={step.id}>
                  <span className="step-index">
                    {String(index + 1).padStart(2, "0")}
                  </span>
                  <div>
                    <h3>{step.title}</h3>
                    <p>{step.body}</p>
                  </div>
                </li>
              ))}
            </ol>
          </section>

          {article.callouts.map((callout) => (
            <Callout tone={callout.tone} title={callout.title} key={callout.title}>
              {callout.body}
            </Callout>
          ))}

          {showScreens && <ScreenshotGallery article={article} />}
          {article.sectionId === "master-data" && (
            <RelevantMasterData articleId={article.id} />
          )}
        </div>

        <aside className="article-rail">
          <div className="rail-card">
            <span className="micro-label">In this guide</span>
            <ol>
              <li><a href="#procedure-title">Procedure</a></li>
              {article.sectionId === "master-data" && <li><a href="#matrix-title">Data map</a></li>}
              {showScreens && <li><a href="#observed-interface-title">Screen reference</a></li>}
            </ol>
          </div>
          <div className="rail-card related-card">
            <span className="micro-label">Continue reading</span>
            {related.map((item) => (
              <button key={item.id} onClick={() => onSelect(item.id)}>
                <span>{item.title}</span>
                <ArrowIcon />
              </button>
            ))}
          </div>
          <div className="rail-note">
            <span>Time zone</span>
            <p>Both apps display flight times in the station’s configured time zone. Scheduling and operation inputs use that same zone.</p>
          </div>
        </aside>
      </div>
    </article>
  );
}

function SearchResults({
  results,
  query,
  onSelect,
}: {
  results: readonly ManualArticle[];
  query: string;
  onSelect: (id: string) => void;
}) {
  return (
    <section className="search-results" aria-live="polite">
      <header>
        <span className="micro-label">Search results</span>
        <h1>
          {results.length} {results.length === 1 ? "guide" : "guides"} for “{query}”
        </h1>
        <p>Results include guide titles, procedures, keywords, and operational locations.</p>
      </header>
      {results.length > 0 ? (
        <div className="result-grid">
          {results.map((article) => (
            <button className="result-card" key={article.id} onClick={() => onSelect(article.id)}>
              <div>
                <AudienceBadge audience={article.audience} />
                <span className="result-eyebrow">{article.eyebrow}</span>
              </div>
              <h2>{article.title}</h2>
              <p>{article.summary}</p>
              <span className="result-open">
                Open guide <ArrowIcon />
              </span>
            </button>
          ))}
        </div>
      ) : (
        <div className="empty-results">
          <span>0</span>
          <h2>No exact match</h2>
          <p>Try a flight number concept, invitation, screen name, or master-data record.</p>
        </div>
      )}
    </section>
  );
}

export default function Home() {
  const [selectedArticleId, setSelectedArticleId] = useState(
    "current-scope",
  );
  const [query, setQuery] = useState("");
  const [audience, setAudience] = useState<"everything" | ManualAudience>("everything");
  const [menuOpen, setMenuOpen] = useState(false);
  const searchRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const syncFromHash = () => {
      const hash = window.location.hash.replace(/^#guide=/, "");
      if (hash && articles.some((article) => article.id === hash)) {
        setSelectedArticleId(hash);
      }
    };
    const handleKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      const typing =
        target?.tagName === "INPUT" ||
        target?.tagName === "TEXTAREA" ||
        target?.isContentEditable;
      if (event.key === "/" && !typing) {
        event.preventDefault();
        searchRef.current?.focus();
      }
      if (event.key === "Escape") {
        setQuery("");
        searchRef.current?.blur();
      }
    };
    const hashTimer = window.setTimeout(syncFromHash, 0);
    window.addEventListener("hashchange", syncFromHash);
    window.addEventListener("keydown", handleKey);
    return () => {
      window.clearTimeout(hashTimer);
      window.removeEventListener("hashchange", syncFromHash);
      window.removeEventListener("keydown", handleKey);
    };
  }, []);

  const selectedArticle =
    articles.find((article) => article.id === selectedArticleId) ?? articles[0];
  const activeSectionId = selectedArticle.sectionId;

  const results = useMemo(() => {
    const needle = normalizeSearchText(query);
    if (!needle) return [];
    return articles.filter((article) => {
      if (
        audience !== "everything" &&
        article.audience !== "all" &&
        article.audience !== audience
      ) {
        return false;
      }
      const haystack = normalizeSearchText([
        article.title,
        article.eyebrow,
        article.summary,
        article.location ?? "",
        ...article.keywords,
        ...article.steps.flatMap((step) => [step.title, step.body]),
        ...article.callouts.flatMap((callout) => [callout.title, callout.body]),
      ].join(" "));
      return needle
        .split(/\s+/)
        .filter(Boolean)
        .every((term) => haystack.includes(term));
    });
  }, [audience, query]);

  const selectArticle = (id: string) => {
    setSelectedArticleId(id);
    setQuery("");
    setMenuOpen(false);
    window.history.replaceState(null, "", `#guide=${id}`);
    window.scrollTo({ top: 0, behavior: "smooth" });
  };

  const selectSection = (sectionId: string) => {
    const category = manualCategories.find((item) => item.sectionId === sectionId);
    const firstId = category?.articleIds[0];
    if (firstId) selectArticle(firstId);
  };

  return (
    <div className="manual-shell">
      <a className="skip-link" href="#main-content">Skip to guide</a>
      <aside className={`sidebar ${menuOpen ? "open" : ""}`} aria-label="Manual navigation">
        <div className="brand-block">
          <LogoMark />
          <div>
            <strong>NAGS</strong>
            <span>Operations field guide</span>
          </div>
          <button className="close-menu" onClick={() => setMenuOpen(false)} aria-label="Close navigation">
            ×
          </button>
        </div>

        <nav className="chapter-nav" aria-label="Guide chapters">
          <span className="nav-label">Manual contents</span>
          {manualSections.map((section, sectionIndex) => {
            const isActive = section.id === activeSectionId;
            const sectionCategories = manualCategories.filter(
              (category) => category.sectionId === section.id,
            );
            return (
              <div className={`chapter ${isActive ? "active" : ""}`} key={section.id}>
                <button className="chapter-button" onClick={() => selectSection(section.id)}>
                  <span>{String(sectionIndex + 1).padStart(2, "0")}</span>
                  <div>
                    <strong>{section.title}</strong>
                    <small>{section.eyebrow}</small>
                  </div>
                  <i aria-hidden="true">⌄</i>
                </button>
                {isActive && (
                  <div className="chapter-articles">
                    {sectionCategories.map((category) => (
                      <div key={category.id}>
                        <span>{category.title}</span>
                        {category.articleIds.map((id) => {
                          const item = articles.find((article) => article.id === id);
                          if (!item) return null;
                          return (
                            <button
                              className={item.id === selectedArticle.id ? "current" : ""}
                              key={item.id}
                              onClick={() => selectArticle(item.id)}
                            >
                              {item.title}
                            </button>
                          );
                        })}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            );
          })}
        </nav>

        <div className="sidebar-footer">
          <div>
            <span className="live-dot" />
            <strong>Operations reference</strong>
          </div>
          <p>Web flight planning · Android flight tools</p>
          <small>Product-aligned · November 2026</small>
        </div>
      </aside>

      {menuOpen && <button className="menu-scrim" onClick={() => setMenuOpen(false)} aria-label="Close menu" />}

      <div className="manual-workspace">
        <header className="topbar">
          <button className="menu-button" onClick={() => setMenuOpen(true)} aria-label="Open navigation">
            <span />
            <span />
            <span />
          </button>
          <label className="search-box">
            <SearchIcon />
            <input
              ref={searchRef}
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Search flights, invitations, Android, master data…"
              aria-label="Search the user manual"
            />
            {query ? (
              <button type="button" onClick={() => setQuery("")} aria-label="Clear search">×</button>
            ) : (
              <kbd>/</kbd>
            )}
          </label>
          <div className="topbar-meta">
            <span>{articles.length} guides</span>
            <i />
            <span>{manualSections.length} chapters</span>
          </div>
        </header>

        <div className="audience-filter" aria-label="Filter search by audience">
          <span>Audience</span>
          {audienceOptions.map((option) => (
            <button
              key={option.id}
              className={audience === option.id ? "active" : ""}
              onClick={() => setAudience(option.id)}
            >
              {option.label}
            </button>
          ))}
        </div>

        <main id="main-content">
          {query.trim() ? (
            <SearchResults results={results} query={query.trim()} onSelect={selectArticle} />
          ) : (
            <ArticleReader article={selectedArticle} onSelect={selectArticle} />
          )}
        </main>

        <footer className="site-footer">
          <div>
            <LogoMark />
            <span>NAGS Operations field guide</span>
          </div>
          <p>Use the application as the source of truth for current records and permissions.</p>
        </footer>
      </div>
    </div>
  );
}
