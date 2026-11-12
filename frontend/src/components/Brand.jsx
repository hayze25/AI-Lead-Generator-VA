/** The business logo (or a default mark) next to the business name. */
export default function Brand({ branding, size = 28, showName = true }) {
  const name = branding?.name || "Business Assistant";
  return (
    <span className="brand-mark">
      {branding?.logoDataUrl ? (
        <img src={branding.logoDataUrl} alt="" className="brand-logo" style={{ height: size, maxWidth: size * 3 }} />
      ) : (
        <span className="logo" aria-hidden="true" style={{ fontSize: size * 0.8 }}>
          ◆
        </span>
      )}
      {showName && <strong className="brand-name">{name}</strong>}
    </span>
  );
}
