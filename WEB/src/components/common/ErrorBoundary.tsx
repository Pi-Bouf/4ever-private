import { Component, type ErrorInfo, type ReactNode } from "react";

interface Props {
  children: ReactNode;
}
interface State {
  error: Error | null;
}

export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }
  componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error("Render error:", error, info);
  }
  render(): ReactNode {
    if (this.state.error) {
      return (
        <div className="m-6 rounded-lg border border-red-900/60 bg-red-950/40 p-4 text-red-200">
          <b>Something went wrong.</b> {this.state.error.message}
        </div>
      );
    }
    return this.props.children;
  }
}
