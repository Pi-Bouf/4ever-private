import type { CSSProperties } from "react";

export function PixelImg({
  src,
  size,
  className = "",
  onError,
}: {
  src: string;
  size?: number;
  className?: string;
  onError?: () => void;
}) {
  const style: CSSProperties | undefined = size ? { width: size, height: size } : undefined;
  return (
    <img src={src} alt="" loading="lazy" onError={onError} className={`pixelated object-contain ${className}`} style={style} />
  );
}
