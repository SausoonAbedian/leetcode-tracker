import React from 'react'
import ReactDOM from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App'
import { ActiveAttemptProvider } from './lib/activeAttempt'
import './index.css'

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <BrowserRouter>
      <ActiveAttemptProvider>
        <App />
      </ActiveAttemptProvider>
    </BrowserRouter>
  </React.StrictMode>,
)
