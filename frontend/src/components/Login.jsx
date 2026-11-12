import { useState } from "react";
import { api, auth } from "../api";
import { ErrorBanner } from "../ui";
import Brand from "./Brand.jsx";

export default function Login({ branding, onSignedIn }) {
  const [mode, setMode] = useState("signin"); // "signin" | "register"
  const [businessName, setBusinessName] = useState("");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const registering = mode === "register";
  const canRegister = branding?.allowRegistration !== false;

  function switchMode(next) {
    setMode(next);
    setError("");
    setPassword("");
    setConfirm("");
  }

  async function submit(e) {
    e.preventDefault();
    setError("");

    if (registering && password !== confirm) {
      return setError("The two passwords don't match.");
    }

    setBusy(true);
    try {
      const { token } = registering
        ? await api.register(businessName.trim(), username.trim(), password)
        : await api.login(username, password);
      auth.set(token);
      onSignedIn();
    } catch (err) {
      setError(err.message);
      setBusy(false);
    }
  }

  return (
    <div className="login-wrap">
      <form className="card login" onSubmit={submit}>
        <div className="login-brand">
          <Brand branding={branding} size={44} />
        </div>
        <p className="muted center">{registering ? "Create your own business account" : "Sign in to continue"}</p>
        <ErrorBanner message={error} />

        {registering && (
          <label>
            Business name
            <input
              value={businessName}
              onChange={(e) => setBusinessName(e.target.value)}
              maxLength={200}
              autoComplete="organization"
              autoFocus
              required
            />
          </label>
        )}
        <label>
          {registering ? "Email" : "Email or username"}
          <input
            type={registering ? "email" : "text"}
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            autoComplete={registering ? "email" : "username"}
            autoFocus={!registering}
            maxLength={320}
            required
          />
        </label>
        <label>
          Password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete={registering ? "new-password" : "current-password"}
            minLength={registering ? 8 : undefined}
            required
          />
        </label>
        {registering && (
          <>
            <label>
              Confirm password
              <input
                type="password"
                value={confirm}
                onChange={(e) => setConfirm(e.target.value)}
                autoComplete="new-password"
                required
              />
            </label>
            <p className="muted small">At least 8 characters. Your account gets its own private workspace; no one else can see your inquiries or leads.</p>
          </>
        )}

        <div className="actions">
          <button className="primary full" type="submit" disabled={busy}>
            {busy ? (registering ? "Creating account…" : "Signing in…") : registering ? "Create account" : "Sign in"}
          </button>
        </div>

        {(canRegister || registering) && (
          <p className="muted small center switch">
            {registering ? "Already have an account?" : "New here?"}{" "}
            <button type="button" className="link" onClick={() => switchMode(registering ? "signin" : "register")}>
              {registering ? "Sign in" : "Create an account"}
            </button>
          </p>
        )}
      </form>
    </div>
  );
}
