import { createContext } from "react";

/** A single follow-up the toast offers — "the thing you just did happened over there, go look". */
export interface ToastAction {
  label: string;
  onClick: () => void;
}

export interface ToastOptions {
  stacktrace?: string;
  /** Id of the captured Error Log entry, when the backend persisted one — enables an admin
   *  deep-link from the toast into the Error Log. */
  errorId?: string;
  /** Turns the toast into a snackbar: clickable, and given long enough to be read and acted on. */
  action?: ToastAction;
}

export interface ToastItem extends ToastOptions {
  id: number;
  message: string;
  type: "success" | "error" | "info";
}

export interface ToastContextValue {
  show: (
    message: string,
    type?: ToastItem["type"],
    options?: ToastOptions,
  ) => void;
}

const ToastContext = createContext<ToastContextValue>({ show: () => {} });
export default ToastContext;
