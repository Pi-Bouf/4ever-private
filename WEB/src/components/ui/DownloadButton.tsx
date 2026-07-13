import type { ReactNode } from "react";

/** An anchor styled as a button that downloads a static file from public/. */
export function DownloadButton({
  href,
  filename,
  children,
  className = "",
}: {
  href: string;
  filename?: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <a
      href={href}
      download={filename}
      className={`inline-flex items-center gap-1.5 rounded-[8px] border border-accent2 bg-accent2/15 px-3 py-2 text-[12.5px] font-semibold text-accent transition-colors hover:bg-accent2/25 ${className}`}
    >
      {children}
    </a>
  );
}
