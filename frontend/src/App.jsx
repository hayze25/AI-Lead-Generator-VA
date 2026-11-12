import { useCallback, useEffect, useState } from "react";
import { api, auth } from "./api";
import Brand from "./components/Brand.jsx";
import Dashboard from "./components/Dashboard.jsx";
import Inbox from "./components/Inbox.jsx";
import Leads from "./components/Leads.jsx";
import Login from "./components/Login.jsx";
import NewInquiry from "./components/NewInquiry.jsx";
import Settings from "./components/Settings.jsx";

const TABS = [
  ["dashboard", "Dashboard"],
  ["inbox", "Inbox"],
  ["leads", "Leads"],
  ["settings", "Settings"],
];

const DEFAULT_NAME = "Business Assistant";

export default function App() {
  const [signedIn, setSignedIn] = useState(() => !!auth.token);
  const [branding, setBranding] = useState(null);
  const [tab, setTab] = useState("dashboard");
  const [showNew, setShowNew] = useState(false);
  const [selectedInquiry, setSelectedInquiry] = useState(null);
  const [stats, setStats] = useState(null);
  const [statsError, setStatsError] = useState("");
  const [refreshKey, setRefreshKey] = useState(0);

  const refresh = () => setRefreshKey((k) => k + 1);

  const loadBranding = useCallback(() => {
    api
      .branding()
      .then(setBranding)
      .catch(() => {}); // not critical: fall back to the default name
  }, []);

  const signOut = useCallback(() => {
    auth.set(null);
    setSignedIn(false);
    setStats(null);
    setSelectedInquiry(null);
    setTab("dashboard");
    setBranding(null); // don't leave one business's name/logo on the sign-in page
    loadBranding();
  }, [loadBranding]);

  // If any request comes back 401 (expired token), return to the login screen.
  useEffect(() => {
    auth.onUnauthorized = () => {
      setSignedIn(false);
      setStats(null);
      setBranding(null);
      loadBranding();
    };
    return () => {
      auth.onUnauthorized = null;
    };
  }, [loadBranding]);

  useEffect(loadBranding, [loadBranding]);

  // Show the business name in the browser tab, and use its logo as the tab icon.
  useEffect(() => {
    document.title = branding?.name || DEFAULT_NAME;
    let link = document.querySelector("link[rel='icon']");
    if (!link) {
      link = document.createElement("link");
      link.rel = "icon";
      document.head.appendChild(link);
    }
    if (branding?.logoDataUrl) link.href = branding.logoDataUrl;
    else link.removeAttribute("href");
  }, [branding]);

  useEffect(() => {
    if (!signedIn) return;
    api
      .stats()
      .then((s) => {
        setStats(s);
        setStatsError("");
      })
      .catch((e) => setStatsError(e.message));
  }, [refreshKey, signedIn]);

  const openInquiry = (id) => {
    setSelectedInquiry(id);
    setTab("inbox");
  };

  if (!signedIn) {
    return (
      <Login
        branding={branding}
        onSignedIn={() => {
          setSignedIn(true);
          loadBranding();
        }}
      />
    );
  }

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <Brand branding={branding} />
          {stats?.llmMode === "demo" && (
            <span
              className="pill"
              title="No LLM API key is configured, so triage and drafts use simple built-in rules. Set Llm__ApiKey to use a real model."
            >
              Demo mode
            </span>
          )}
        </div>
        <nav aria-label="Main">
          {TABS.map(([key, label]) => (
            <button
              key={key}
              className={`tab ${tab === key ? "active" : ""}`}
              onClick={() => setTab(key)}
              aria-current={tab === key ? "page" : undefined}
            >
              {label}
              {key === "inbox" && stats?.openInquiries > 0 && <span className="count">{stats.openInquiries}</span>}
            </button>
          ))}
        </nav>
        <button className="primary" onClick={() => setShowNew(true)}>
          + New inquiry
        </button>
        <button onClick={signOut}>Sign out</button>
      </header>

      <main>
        {tab === "dashboard" && <Dashboard stats={stats} error={statsError} />}
        {tab === "inbox" && (
          <Inbox
            refreshKey={refreshKey}
            selectedId={selectedInquiry}
            onSelect={setSelectedInquiry}
            onChange={refresh}
          />
        )}
        {tab === "leads" && <Leads refreshKey={refreshKey} onChange={refresh} onOpenInquiry={openInquiry} />}
        {tab === "settings" && <Settings onSaved={loadBranding} />}
      </main>

      {showNew && (
        <NewInquiry
          onClose={() => setShowNew(false)}
          onCreated={(inquiry) => {
            setShowNew(false);
            refresh();
            openInquiry(inquiry.id);
          }}
        />
      )}
    </div>
  );
}
