import { Button } from "./Button";

export function CloseButton({ onClick }: { onClick: () => void }) {
  return (
    <Button
      onClick={onClick}
      aria-label="Close"
      className="absolute right-3 top-2.5 px-2 py-1 text-2xl leading-none text-muted hover:text-text"
    >
      ×
    </Button>
  );
}
