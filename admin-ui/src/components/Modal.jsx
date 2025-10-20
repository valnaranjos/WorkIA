import React from "react";
import PropTypes from "prop-types";

Modal.propTypes = {
  title: PropTypes.string,
  open: PropTypes.bool,
  onClose: PropTypes.func,
  children: PropTypes.node,
  footer: PropTypes.node,
};

export default function Modal({ title, open, onClose, children, footer }) {
  if (!open) return null;
  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center">
      <div className="bg-white rounded-lg shadow-xl w-[min(900px,92vw)] max-h-[85vh] overflow-hidden flex flex-col">
        {/* Header */}
        <div className="px-4 py-3 border-b flex items-center justify-between">
          <div className="font-medium">{title}</div>
          <button onClick={onClose} className="text-gray-600 hover:text-gray-800">✕</button>
        </div>

        {/* Body */}
        <div className="p-4 overflow-auto flex-1">{children}</div>

        {/* Footer */}
        <div className="px-4 py-3 border-t text-right">
          {footer}
        </div>
      </div>
    </div>
  );
}
