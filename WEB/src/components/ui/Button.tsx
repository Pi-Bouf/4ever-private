import type { ButtonHTMLAttributes, ReactNode } from "react";

export function Button({ children, className = "", ...rest }: ButtonHTMLAttributes<HTMLButtonElement> & { children: ReactNode }) {
  return (
    <button {...rest} className={className}>
      {children}
    </button>
  );
}
