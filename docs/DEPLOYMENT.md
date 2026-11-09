# Hướng dẫn đóng gói và vận hành

## Quyền và vị trí dữ liệu

| Thành phần | Windows | Linux |
|---|---|---|
| Binaries | `%ProgramFiles%\DicomAgent\host`, `desktop` | `/opt/dicom-agent/host`, `desktop` |
| Data | `%ProgramData%\DicomAgent` | `/var/lib/dicom-agent` |
| Service identity | `NT SERVICE\DicomAgent` | `dicom-agent` |
| Operator group | `DicomAgentOperators` | `dicom-agent-operators` |
| Management | Named pipe, ACL, deny Network SID | Unix socket mode 0660, data directory 2750 |

Service có quyền modify data; operator đọc ảnh/metadata và gọi IPC để ghi cấu hình. Không cấp operator quyền ghi binary/service definition. Quyền IPC đồng nghĩa quyền cấu hình Receiver. Giới hạn thành viên group cho người được tin cậy.

## Windows MSI/EXE

Scripts publish/install/uninstall đã có; **chưa tạo và ký installer MSI/EXE**. Quy trình đóng gói đề xuất:

1. Chạy `publish.ps1` trên CI Windows, lấy đủ host/desktop artifacts và native libraries.
2. Dùng WiX Toolset 4/5 để author MSI: mỗi binary thành component, service cài dưới virtual account, automatic start; bảo vệ data bằng ACL tương đương `install.ps1`.
3. WiX Burn tạo bootstrapper EXE nếu muốn. Bản self-contained không cần cài runtime .NET.
4. Đánh dấu data/storage/DB/settings là dữ liệu khách hàng vĩnh viễn, không thuộc nhóm file bị xóa khi uninstall/major upgrade.
5. Tạo local operator group, chọn operator rõ ràng trong installer, cần elevation để cài service/ACL. Firewall chỉ Domain/Private với explicit device IPs.
6. Dừng service lúc upgrade, sao lưu cấu hình/DB, thay binary, khởi động lại, kiểm tra C-ECHO và migration/history. Không dùng downgrade binary với schema mới nếu chưa xác nhận tương thích.
7. Ký số binaries và MSI/EXE bằng chứng thư của tổ chức; test clean Windows VM và uninstall/upgrade trước phát hành.

Lưu ý `sc.exe`/firewall/local-group APIs chỉ hoạt động Windows. Parse script và cross-publish từ Linux không kiểm chứng các thao tác này.

## Gói Debian `.deb`

**Chưa build gói `.deb`**. Có thể dùng debhelper hoặc `dpkg-deb`:

1. Publish linux-x64 self-contained trên môi trường build có dependencies phù hợp target distro.
2. Package payload vào `/opt/dicom-agent/{host,desktop}`, unit vào `/lib/systemd/system/dicom-agent.service` theo distro. Binaries root-owned, không group/other write.
3. `DEBIAN/control` khai báo architecture amd64 và native dependencies thực tế của Avalonia/Skia (xác định bằng `ldd`, chạy kiểm thử trên Ubuntu LTS mục tiêu); Host không cần desktop packages nếu tách package UI.
4. `postinst`: tạo service user/group, data directory owner/mode theo install script, `systemctl daemon-reload`, enable/start. Thêm operator là bước quản trị có chủ đích; không đoán username trong maintainer script.
5. `prerm`: dừng/disable service. `postrm` không tự xóa storage/history/settings, kể cả purge; chính sách xóa dữ liệu cần lệnh riêng, rõ ràng và có đồng ý.
6. Chạy `dpkg-deb --build package-root dicom-agent_0.1.0_amd64.deb`; test install/upgrade/remove trên VM Ubuntu mục tiêu, kiểm tra C-ECHO/C-STORE, permissions và backup/restore.

## Custom storage Linux

Ví dụ thư mục `/srv/dicom-storage`:

```bash
sudo install -d -o dicom-agent -g dicom-agent-operators -m 2750 /srv/dicom-storage
sudo systemctl edit dicom-agent.service
# Thêm:
# [Service]
# ReadWritePaths=/srv/dicom-storage
sudo systemctl daemon-reload
# Lưu StoragePath trong UI, rồi:
sudo systemctl restart dicom-agent.service
```

Không chọn path qua symlink. `ProtectHome=true` chặn home directory; nên chọn thư mục dữ liệu service thay vì home cá nhân. Volume mount ngoài cần permissions ổn định. Không dùng chung một storage root giữa nhiều Host.

## Backup và phục hồi

- Dừng service trước khi sao lưu nhất quán hoặc dùng SQLite backup API; không chỉ copy `history.db` đang ghi mà bỏ WAL/SHM.
- Giữ nguyên `.dcm` nguyên gốc, `settings.json`, DB và bản sao quyền truy cập.
- Lịch sử có thể rebuild từ file dưới root đang cấu hình sau khi SQLite/schema phục hồi. Reconciliation chạy theo chu kỳ 30 giây.
- Không xóa `.dcm` chỉ vì thiếu history. `.partial` sau crash cần kiểm tra thủ công, không đổi đuôi hàng loạt.
- Cùng UID khác nội dung cần điều tra nguồn gửi và chính sách lưu version có chủ đích; không overwrite để làm test pass.
- Theo dõi dung lượng và lỗi journal/Event Log; Agent không xóa ảnh hay xoay vòng retention tự động.

## Kiểm thử tại hiện trường

Kiểm tra ít nhất: startup sau reboot; đóng/mở lại UI; C-ECHO; C-STORE DX/CR/CT/MR đúng transfer syntax; ảnh lớn thực tế; mất mạng/retry; duplicate; hết đĩa; DB lock/lỗi quyền; restart khi đang gửi; allowlist và firewall; quyền operator; xem file bằng viewer độc lập; upload thủ công lên website; upgrade và uninstall không mất ảnh.
