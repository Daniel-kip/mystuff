-- setup_database.sql
-- Create database if it doesn't exist
CREATE DATABASE IF NOT EXISTS deltech_db;
USE deltech_db;

-- Disable foreign key checks to allow dropping tables
SET FOREIGN_KEY_CHECKS = 0;

-- Drop tables in correct order (child tables first)
DROP TABLE IF EXISTS campaign_recipients;
DROP TABLE IF EXISTS sms_campaigns;
DROP TABLE IF EXISTS user_quotas;
DROP TABLE IF EXISTS api_keys;
DROP TABLE IF EXISTS audit_logs;
DROP TABLE IF EXISTS auth_refresh_tokens;
DROP TABLE IF EXISTS message_logs;
DROP TABLE IF EXISTS user_settings;
DROP TABLE IF EXISTS auth_users;

-- Re-enable foreign key checks
SET FOREIGN_KEY_CHECKS = 1;

-- Create users table
CREATE TABLE auth_users (
    id INT AUTO_INCREMENT PRIMARY KEY,
    full_name VARCHAR(255) NOT NULL,
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    salt VARCHAR(255) NOT NULL,
    role VARCHAR(50) DEFAULT 'User',
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

-- Create refresh tokens table
CREATE TABLE auth_refresh_tokens (
    id INT AUTO_INCREMENT PRIMARY KEY,
    token_hash VARCHAR(255) NOT NULL,
    user_id INT NOT NULL,
    expires_at DATETIME NOT NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    revoked BOOLEAN DEFAULT FALSE,
    device VARCHAR(500),
    ip VARCHAR(45),
    FOREIGN KEY (user_id) REFERENCES auth_users(id) ON DELETE CASCADE
);

-- Create message logs table
CREATE TABLE message_logs (
    id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    recipient VARCHAR(20) NOT NULL,
    message_text TEXT NOT NULL,
    message_id VARCHAR(100),
    status VARCHAR(50) DEFAULT 'Pending',
    response TEXT,
    cost DECIMAL(10,2) DEFAULT 0.00,
    sent_at DATETIME NOT NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    device_info VARCHAR(500),
    ip_address VARCHAR(45),
    FOREIGN KEY (user_id) REFERENCES auth_users(id) ON DELETE CASCADE
);

-- Create user settings table
CREATE TABLE user_settings (
    user_id INT PRIMARY KEY,
    notifications BOOLEAN DEFAULT TRUE,
    email_updates BOOLEAN DEFAULT FALSE,
    sms_alerts BOOLEAN DEFAULT FALSE,
    dark_mode BOOLEAN DEFAULT FALSE,
    compact_mode BOOLEAN DEFAULT FALSE,
    language VARCHAR(10) DEFAULT 'en',
    currency VARCHAR(10) DEFAULT 'KES',
    timezone VARCHAR(50) DEFAULT 'Africa/Nairobi',
    profile_visibility VARCHAR(20) DEFAULT 'public',
    search_engine_indexing BOOLEAN DEFAULT TRUE,
    data_tracking BOOLEAN DEFAULT FALSE,
    two_factor_auth BOOLEAN DEFAULT FALSE,
    login_alerts BOOLEAN DEFAULT TRUE,
    reduce_motion BOOLEAN DEFAULT FALSE,
    high_contrast BOOLEAN DEFAULT FALSE,
    auto_renew BOOLEAN DEFAULT TRUE,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES auth_users(id) ON DELETE CASCADE
);

-- Create indexes for better performance
CREATE INDEX idx_auth_users_email ON auth_users(email);
CREATE INDEX idx_refresh_tokens_hash ON auth_refresh_tokens(token_hash);
CREATE INDEX idx_refresh_tokens_user ON auth_refresh_tokens(user_id);
CREATE INDEX idx_message_logs_user ON message_logs(user_id);
CREATE INDEX idx_message_logs_sent ON message_logs(sent_at);
CREATE INDEX idx_message_logs_status ON message_logs(status);

-- Insert default admin user (password: Admin123!)
INSERT INTO auth_users (full_name, email, password_hash, salt, role) 
VALUES (
    'System Administrator', 
    'admin@deltech.com', 
    '8hJv7Kk2MnBpQwE1xZ3RtY6uI9oP0aL4cVbN7mGsDfXhC5jWqTzYr', 
    's9F2kL8pQ1mN6rT3wX7zY4bV5cM8nB0q', 
    'Admin'
);

-- Insert sample user (password: User123!)
INSERT INTO auth_users (full_name, email, password_hash, salt, role) 
VALUES (
    'Test User', 
    'user@deltech.com', 
    '6tG5hJ2kL9pQ1wE4rT7yU8iO3pA5sD2fG', 
    'p3L8kQ2mN6rT9wX1zY4bV7cM0nB3qS5t', 
    'User'
);

-- Insert default settings for existing users
INSERT INTO user_settings (user_id) 
SELECT id FROM auth_users;

SELECT 'Database setup completed successfully!' AS status;
